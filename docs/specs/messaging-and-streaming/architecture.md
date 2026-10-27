# 3. Architecture

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 3.1 Overview

```
                  ┌──────────────────────────────── Hookpipe ─────────────────────────────────┐
                  │                                                                           │
HTTP webhook ──►  │ WebhookHandler → validate → envelope → route → targets (broker:topic)     │
POST /webhooks/…  │        │ enqueue (bounded)                                                │
                  │        ▼                                                                  │
                  │   ┌─────────────┐  1..N workers  ┌────────┐                               │
                  │   │publish queue│ ─────────────► │  IBus  │ ──────────────────► broker A  │
                  │   └─────────────┘   per broker   └────────┘                               │
                  │          ┆ local delivery (improvement, §4.9): in memory, after the       │
                  │          ┆ broker confirms; the broker's copy is dropped by message ID    │
                  │          ▼                                                                │
broker A ──────►  │ runax consumer → stream inbox → StreamWorker → per-client queues          │
                  │                  (bounded)      (one/stream,        │                     │
                  │                                  dedupe, debounce)  ▼                     │
browser ◄───────  │                 protocol adapter: SSE  (WebSocket, WebRTC)                │
GET /streams/…    └───────────────────────────────────────────────────────────────────────────┘
```

## 3.2 Terms

| Term | Meaning | Config |
| --- | --- | --- |
| **Broker** | A connection to one messaging system, backed by exactly one runax-messaging bus. Webhooks publish to it; streams read from it. | `brokers` |
| **Target** | A broker plus a topic, written inline where it's used: `rabbit:github.push`. There's no separate sink section. | `to`, `from` ([§5.5](configuration.md#55-targets)) |
| **Webhook** | An HTTP route under `/webhooks` that accepts calls and publishes them to targets | `webhooks` |
| **Stream** | Reads targets and pushes their messages to connected clients, under `/streams` | `streams` |
| **Publish worker** | The single loop that publishes for a broker. One per broker by default. | `brokers.<id>.publish` |
| **Stream worker** | The single loop that owns a stream | — |
| **Protocol** | How a stream reaches a client: `sse` now, `websocket` and `webrtc` later | `streams.<id>.protocols` |

## 3.3 Publish workers: one per broker by default

**Invariants:**

1. Each broker that Hookpipe publishes to has one `PublishWorker` by default, or
   `publish.workers: N` of them ([Going faster](#going-faster-batching-and-more-workers)).
   - Each worker has its own bounded `Channel<PublishItem>` (`SingleReader = true`,
     `SingleWriter = false`).
   - Each item goes to exactly one worker, picked by its `order_by` key (by default its topic).
2. Only a broker's publish workers publish to it. Handlers never touch `IBus` directly.
3. A worker publishes its items in queue order, and a retrying item blocks the items behind it
   in that worker.
   - With one worker, everything published to the broker keeps its order.
   - With several, items that share an `order_by` key do. By default that's every topic.
4. Each stream has exactly one `StreamWorker`. It's the only code that assigns event IDs,
   writes the replay buffer, or changes the subscriber list (see
   [§4.3](streaming.md#43-stream-worker)).

**What this buys:**

- **Ordering.** Messages leave a worker in the order they were accepted, including across
  retries. v1 can't promise that.
- **Safe client use.** Nothing calls a broker client concurrently on a worker's behalf.
- **Back-pressure.** Queue depth is the lag. A full queue becomes a 503 at the edge instead of
  hidden latency.
- **Isolation.**
  - A stuck broker fills its own queues and never takes threads from other brokers.
  - Within one broker, a failing topic holds up only the worker it lands on. With
    `workers: N` and the default `order_by: "{topic}"`, topics on other workers keep moving.
- **Throughput through batching.** The worker drains whatever is waiting, up to
  `batch.max_size`, into one `PublishBatchAsync` call per topic: one SQS `SendMessageBatch`,
  one RabbitMQ confirm round trip. One sequential worker would otherwise be RTT-bound.
- **Clean shutdown.** Completing the channel and letting the worker drain is the whole
  shutdown protocol.

**Workers are async loops, not OS threads.** Each worker is one long-running single-reader
`async` loop. That gives the same guarantee a dedicated thread would: one operation at a time,
in order.

- **Threads wouldn't be faster.** A publish spends almost all its time waiting for the broker
  to acknowledge it. A dedicated thread would sit blocked during that wait. An async loop hands
  the thread back so another worker can use it. Latency per message is the same either way.
- **Loops scale; threads don't.** Hundreds of loops run on a handful of thread-pool threads. A
  thread per worker costs a stack each, plus context switches.
- **The thread wouldn't stick.** After the first `await`, the code resumes on the thread pool
  unless a custom scheduler pins it.

A dedicated thread pays off only for CPU-heavy work or a blocking SDK, and no transport here is
either. Parallelism comes from running many workers at once, not from giving each its own
thread.

**Workers share the bus.** All of a broker's workers share its `IBus`. Buses are thread-safe,
and transports pool their own channels or producers (for example
`RabbitMqConfig.PublishChannelPoolSize`).

### Going faster: batching and more workers

Speed and parallelism come from three places. Reach for them in this order:

1. **Brokers run in parallel with each other.** Every worker is independent, so five brokers
   publish five batches at once. HTTP handlers only enqueue, which takes microseconds, so
   ingress is as parallel as Kestrel.
2. **Batching lifts the round-trip limit inside one worker.** Publishing one message at a time
   caps a worker near 1 / RTT: about 500 messages/s at a 2 ms broker round trip. With
   `batch.max_size: 100` the same worker can move up to about 100 times that, still in order.
   It's the cheapest speedup.
3. **More workers per broker.** `publish.workers: N` gives a broker N workers, each with its own
   queue.
   - An item is routed by its `order_by` key. The same key always lands on the same worker, so
     ordering holds per key while different keys publish in parallel.
   - The default key is `{topic}`, so each topic stays in order and topics spread across
     workers.
   - A finer key, such as `{$.customer.id}` or `{webhook_id}`, spreads one busy topic.
   - `none` sends each item to the least-loaded worker and gives no ordering at all.
   - This is the same trade-off Kafka makes with partitions.

```yaml
brokers:
  kafka:
    type: kafka
    settings: { bootstrap_servers: ${KAFKA_BROKERS} }
    publish:
      workers: 8
      order_by: "{$.customer.id:-unknown}"   # same customer → same worker → in order
      batch: { max_size: 100, max_delay: 5ms }
```

A key that carries most of the traffic still lands on a single worker. More workers don't help
a hot key; batching does.

Not offered: overlapping several publishes from one worker (pipelining). The RabbitMQ transport
keeps a pool of publish channels, so two publishes in flight can take different channels and
arrive out of order. Batching gets most of the same gain without that risk.

## 3.4 Ingress: HTTP → brokers

```
WebhookHandler.HandleAsync                                   (POST /webhooks/<path>)
  1. resolve live webhook, method check, rate limit, validators (all must pass)   (v1 order)
  2. build the envelope and serialize it once; both are immutable from here on
  3. pick targets: `to`, or the first matching `route` rule (`to: []` acknowledges and drops)
  4. for each target: render topic, partition key, and order_by; pick the broker's worker;
     enqueue a PublishItem
  5. respond per the webhook's `delivery` settings
```

**Delivery modes** (`webhooks.<id>.delivery.ack`):

| Mode | Responds when | Use when |
| --- | --- | --- |
| `delivered` (default, the v1 behavior) | Each target has been published or has failed, or `delivery.timeout` passed | The caller should retry if Hookpipe couldn't publish |
| `enqueued` | Every target's worker has accepted the item into its queue | The caller needs a fast 202 and in-memory buffering is acceptable (the "request buffering" roadmap item) |

`delivery.accept_when: any | all` (default `any`, the v1 rule) decides whether one successful
target is enough for a 202.

**Responses:**

| Situation | Status | Body |
| --- | --- | --- |
| `accept_when` satisfied | `202` | `{"status":"accepted","webhook_id":…,"message_id":…}` (v1: `endpoint_id`, and no `message_id`) |
| Publishing failed (`delivered`) | `500` | `{"error":"publish_failed",…}` (v1: `all_sinks_failed`) |
| A target's queue is full past `enqueue_timeout`, or Hookpipe is stopping | `503` + `Retry-After` | `{"error":"broker_unavailable",…}` |
| `delivery.timeout` elapsed (`delivered`) | `504` | `{"error":"delivery_timeout",…}` |
| 401, 404, 405, 413, 429 | unchanged | unchanged |

**Timeouts and duplicates.**

- When `delivery.timeout` passes, the handler marks its items *abandoned*. A worker skips an
  abandoned item it hasn't started, so a 504 usually means "not published".
- An item already in flight can't be recalled. It may still publish, and the sender's retry then
  produces a duplicate under a new envelope ID.
- Webhooks are at-least-once anyway. Consumers that need dedup should key on the provider's
  delivery ID (for example `X-GitHub-Delivery`).

**Caller disconnects don't cancel.** Once enqueued, an item is published even if the caller
goes away. The item never captures `RequestAborted`.

**Trace context crosses the queue explicitly.** `Activity.Current` doesn't flow through a
`Channel`. Each item captures the request's `ActivityContext`. The worker starts a publish
activity linked to each item it publishes (a batch gets one span with a link per item), so
runax-messaging's publish spans and native `traceparent` headers continue the webhook's trace.

## 3.5 Publish worker

```csharp
internal sealed class PublishItem
{
    public required string MessageId { get; init; }              // envelope ID
    public required JsonElement Payload { get; init; }           // envelope, serialized once (§6.1)
    public required string Topic { get; init; }                  // rendered from the target
    public string? PartitionKey { get; init; }                   // rendered; needs P2
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public ActivityContext Parent { get; init; }
    public TaskCompletionSource<PublishOutcome>? Completion { get; init; }   // null when ack = enqueued

    private int _state;                                          // Pending → Started | Abandoned
    public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    public bool TryAbandon() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;
}

internal sealed class PublishWorker(string brokerId, int index, IBus bus, BrokerPublisher publisher, ILogger logger)
{
    private readonly Channel<PublishItem> _queue = Channel.CreateBounded<PublishItem>(
        new BoundedChannelOptions(publisher.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public async Task RunAsync(CancellationToken hardStop)
    {
        var batch = new List<PublishItem>();
        while (await _queue.Reader.WaitToReadAsync(hardStop))
        {
            var settings = publisher.Current;          // hot-reloadable: batch, retry
            await FillBatchAsync(batch, settings.Batch, hardStop);
            await PublishAsync(batch, settings.Retry, hardStop);   // completes every item
            publisher.LocalDelivery?.Offer(batch);                  // improvement, §4.9
            batch.Clear();
        }
    }
}
```

- **Enqueue.**
  - `when_full: wait` waits up to `enqueue_timeout`, then returns 503.
  - `when_full: reject` returns 503 at once when the queue is full.
  - `drop_oldest` isn't allowed on a publish queue. It would silently lose a message that may
    already have been acknowledged.
- **Batching.** After the first item the worker keeps reading while items are available, up
  to `batch.max_size`, and waits at most `batch.max_delay` for more. With the defaults
  (`max_size: 1`) it publishes one at a time. A batch is split by rendered topic, and
  abandoned items are dropped from it before publishing.
- **Publishing.** `bus.PublishBatchAsync` is used when the batch has more than one item and
  runax-messaging supports per-message headers in batches (P4). Until then the worker publishes
  each item with `bus.PublishAsync(topic, payload, headers)`.
- **Retry.** The Polly pipeline from `brokers.<id>.publish.retry` wraps each publish call, and
  now honors the backoff type and factor. When retries run out, the worker fails the items with
  `PublishOutcome.Failed(exception)`, logs each envelope ID, and moves on. A dead-letter store is
  out of scope.
- **Supervision.** The worker catches per batch. If the loop itself faults, the host fails
  in-flight items, restarts the loop with backoff, and readiness reports the broker as degraded
  until it's running again.

## 3.6 Egress: broker → stream → clients

```
runax consumer (delegate, P1 + P6) ──WriteAsync──►  stream inbox (bounded)
                                                        │
StreamWorker (one per stream) ◄─────────────────────────┘   also reads the control channel first
  1. drop duplicates by message ID, then filter at stream level (static conditions)
  2. assign id = {epoch}-{sequence}; append to replay ring
  3. for each subscriber whose filter matches: outbox.TryWrite(frame)
       full → slow_client policy: disconnect (default) | drop_oldest + gap frame
Protocol adapter (one per connection) reads its outbox and writes frames to the wire
```

- **Acknowledging the broker.** The consumer delegate returns as soon as the inbox accepts the
  event, and the bus then acknowledges the broker message. Streams are a live feed: delivery to
  clients is at-most-once beyond what the replay buffer covers.
- **Back-pressure toward the broker.** A full inbox makes the delegate wait. That slows
  consumption through the broker's own flow control (RabbitMQ prefetch, Kafka poll), so nothing
  is dropped before the worker sees it. The worker never blocks on a client (`TryWrite` only),
  so the inbox drains as fast as the worker can filter.
- **Several streams on one topic** each register their own delegate. runax-messaging groups
  consumers by topic on the bus, so the broker sees one subscription.
- **Local delivery (an improvement, [§4.9](streaming.md#49-improvement-local-delivery-dedupe-and-debounce)).**
  When this replica published the message itself, the stream can get it straight from the
  publish worker, before the broker delivers its copy. The worker then drops the broker's copy
  by message ID.

## 3.7 Every replica must see every message

Queue consumers usually compete, so each message is handled once. Streams want the opposite: a
client connected to replica A needs messages that the broker might have delivered to replica B.
A broker that a stream reads from therefore defaults to `subscribe.mode: broadcast`, a
per-replica subscription. What that takes depends on the transport:

| Transport | Today in runax-messaging | Broadcast in Hookpipe v2 |
| --- | --- | --- |
| RabbitMQ | Each subscriber declares an exclusive, auto-delete, server-named queue bound to the exchange | Already broadcast. Nothing to do. |
| Kafka | `ConsumerGroupId` per bus | Hookpipe sets `consumer_group_id: hookpipe-{instance_id}` and `auto_offset_reset: latest` unless the config sets them |
| Redis Streams | `ConsumerGroup` per bus | Hookpipe sets `consumer_group: hookpipe-{instance_id}`. Groups outlive replicas and need cleanup (P5). |
| Azure Event Hubs | `ConsumerGroup` per bus | Groups must be pre-created, and namespaces cap how many exist. Configure one per replica. |
| Google Pub/Sub, Azure Service Bus | `TopicSubscriptionMap`, pre-provisioned subscriptions | One subscription per replica, provisioned outside Hookpipe and named with `${INSTANCE_ID}`. Auto-creation is P5. |
| SQS, SNS (via SQS) | Queue per topic, competing consumers | Not broadcast. The loader rejects it unless the broker sets `subscribe.mode: shared`, which accepts that each replica sees only some messages. |

`server.instance_id` defaults to `${HOSTNAME}`, which is unique per container.
