# 4. Streaming

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 4.1 Lifecycle of a client connection

```
GET /streams/github-live  (server.paths.streams + path; Accept: text/event-stream, optional Last-Event-ID)
  → match stream by path, extract path params
  → auth (bearer | api-key | ip-allowlist; optional query-parameter token)
  → CORS, max_clients check (503 when full)
  → pick protocol: first adapter whose Accepts(context) is true; 406 if none
  → hub.SubscribeAsync(filter, resumeFrom)    → StreamSubscription
  → protocol.RunAsync(context, subscription)  → writes frames until either side ends
  → finally hub.Unsubscribe(subscription, reason)
```

## 4.2 Protocol seam

```csharp
/// <summary>How a stream reaches one client: SSE today, WebSocket and WebRTC later.</summary>
public interface IStreamProtocol
{
    /// <summary>Config key and metrics label: "sse", "websocket", "webrtc".</summary>
    string Name { get; }

    /// <summary>Whether this request asks for this protocol (Accept header, Upgrade header, signaling route).</summary>
    bool Accepts(HttpContext context);

    /// <summary>Runs one client connection: copies frames from the subscription to the wire until either side ends.</summary>
    Task RunAsync(HttpContext context, StreamSubscription subscription, CancellationToken cancellationToken);
}

/// <summary>A protocol-agnostic handle to one connected client.</summary>
public sealed class StreamSubscription
{
    public string ClientId { get; }
    public string StreamId { get; }
    public ChannelReader<StreamFrame> Frames { get; }       // events plus control frames
    public StreamPosition? ResumedFrom { get; }
    // Reserved for bidirectional protocols: ChannelWriter<InboundMessage>? Inbound
}

public abstract record StreamFrame;
public sealed record EventFrame(string Id, string Name, ReadOnlyMemory<byte> Data) : StreamFrame;
public sealed record GapFrame(string? LastSeenId, string OldestAvailableId) : StreamFrame;
public sealed record ShutdownFrame(TimeSpan ReconnectAfter) : StreamFrame;
```

Adapters only frame and transport. Auth, filtering, replay, IDs, back-pressure, and metrics
live in the stream core, so every protocol gets them the same way. `EventFrame.Data` is
serialized once by the worker and shared by every subscriber.

## 4.3 Stream worker

The worker reads two channels:

- **Control** (unbounded, low volume): `Subscribe`, `Unsubscribe`, and `Reconfigure` commands.
- **Inbox** (bounded, `buffer.inbox`): events from the broker and, with local delivery, from
  this replica's publish workers ([§4.9](#49-improvement-local-delivery-dedupe-and-debounce)).

It always drains control before taking the next event. Connects therefore never queue behind
a burst. Because one loop handles both:

- **Subscribe and replay are atomic.** The worker replays from the ring and adds the
  subscriber before it processes the next event, so a resuming client sees no gap and no
  duplicate between the replayed and the live part.
- **IDs are `{epoch}-{sequence}`.** `epoch` is when the worker started (Unix milliseconds) and
  `sequence` increases by one per event.
- **Resume works within one replica.** A `Last-Event-ID` with this worker's epoch and a sequence
  still in the ring replays everything after it. Anything else gets a `GapFrame`, then live
  events: a restart, another replica, or an ID that has fallen out of the ring. Resuming across
  replicas needs broker offsets ([open question 3](open-questions.md#12-open-questions)).
- **Slow clients.** Each subscriber's outbox holds `buffer.per_client` frames. When one is
  full, `slow_client: disconnect` (the default) closes the client so it reconnects and resumes
  from the ring. `drop_oldest` drops the oldest frame and queues a `GapFrame`.
- **Fan-out cost.** Each event costs the worker one `TryWrite` per matching client. The payload
  is serialized once and shared, and each connection writes to the network on its own task, in
  parallel.
- **Filters with placeholders are indexed.** For a filter like
  `{ jsonpath: "$.tenantId", eq: "{tenant}" }`, the worker groups subscribers by their resolved
  value. It evaluates the path once per event and looks up the matching group, so a per-tenant
  stream costs one evaluation per event, not one per client.
- **Splitting fan-out later.** If one stream with tens of thousands of clients ever makes its
  worker CPU-bound, the subscriber set can be split across several fan-out workers fed by the
  stream worker. That's a later knob, not v1.

## 4.4 What a client receives

| Setting | Values | Default |
| --- | --- | --- |
| `event.name` | A template: `"{topic}"`, a fixed string, `"{header.<name>}"`, or a body value such as `"{$.type}"` | `"{topic}"` |
| `event.data` | `body`: the message body with `__runax` removed, as runax-messaging delivers it. `envelope`: `{"topic","headers","body","received_at"}` | `body` |

When the source topic carries Hookpipe's own webhooks, `body` is the familiar Hookpipe
envelope. Webhook → broker → browser works with no glue code.

## 4.5 Server-side filters

```yaml
filter: { jsonpath: "$.status", eq: paid }            # one condition

filter:                                               # a list: all must match
  - { jsonpath: "$.tenantId", eq: "{tenant}" }        # {tenant} comes from the stream path
  - { header: X-GitHub-Event, eq: push }

filter:                                               # any / all / not, as in webhook routes
  any:
    - { jsonpath: "$.status", in: [paid, refunded] }
    - { jsonpath: "$.amount", gte: 100000 }
```

- **Same syntax as webhooks.** Filters use the condition syntax of a webhook's `route.when`,
  JSONPath included ([§5.7](configuration.md#57-conditions-jsonpath-and-templates)).
- **Filters with path-parameter placeholders** are resolved per subscriber and indexed
  ([§4.3](#43-stream-worker)).
- **Filters without placeholders** are checked once per event, before fan-out.

> **Security note:** A path-parameter filter is routing, not authorization. Until auth can bind a
> parameter to a token claim ([open question 5](open-questions.md#12-open-questions)), anyone with a valid stream
> token can read any tenant by changing the URL. Use a token per tenant, or don't expose
> tenant-scoped streams to untrusted clients.

## 4.6 SSE adapter

- **Accepts** `GET` requests with `Accept: text/event-stream`.
- **Response headers:** `Content-Type: text/event-stream`, `Cache-Control: no-cache`, and
  `X-Accel-Buffering: no` (for nginx). The adapter calls
  `IHttpResponseBodyFeature.DisableBuffering()` and leaves the route out of response
  compression.
- **Framing.** Each event is `id:`, `event:`, then one `data:` line per line of the payload (a
  raw-string body can contain newlines), then a blank line. The adapter flushes after each
  frame, or after each batch of frames already queued.
- **On connect** it sends `retry: <protocols.sse.retry>` once.
- **Heartbeat.** A `: keepalive` comment every `protocols.sse.heartbeat` (default 15 s). That
  keeps proxies with 60 s idle timeouts (nginx, AWS ALB) from closing quiet streams.
- **Resume** from the `Last-Event-ID` header, which browsers send automatically on reconnect,
  or from a `last_event_id` query parameter for the first connect.
- **Control frames.** A gap is `event: hookpipe.gap` with `{"last_seen":…,"oldest_available":…}`.
  Shutdown is `event: hookpipe.shutdown`, a short `retry:`, then the connection closes.
- **`max_connection_age`** (optional) closes long-lived connections, so clients rebalance
  across replicas after a scale-out.
- **Implementation.** ASP.NET Core 10's `TypedResults.ServerSentEvents` / `SseItem<T>` is the
  first choice if it can emit comment heartbeats and flush per frame. If it can't, the frame
  writer is about 50 lines over `HttpResponse.BodyWriter`.

**Things that bite with SSE:**

- **Browsers can't set headers on `EventSource`.** Bearer auth from a browser needs the
  `query_parameter` token option (`?access_token=…`), a cookie, or a fetch-based SSE client.
  Query tokens show up in proxy logs, so keep them short-lived. Serilog's request logging
  records the path only, not the query.
- **HTTP/1.1 browsers allow 6 connections per origin.** Serve streams over HTTP/2 at the
  proxy.
- **Cross-origin `EventSource` needs CORS**, and `withCredentials` when cookies are used.
  `streams.<id>.cors` covers this.

## 4.7 Shutdown

When the host stops:

1. Webhooks start returning 503 with `Connection: close`.
2. The runax consumers stop, so no new events arrive.
3. Each stream worker drains its inbox, sends `ShutdownFrame` to every subscriber, and
   completes their outboxes.
4. Adapters write the shutdown event and close.

All of this happens within `server.shutdown_timeout`.

## 4.8 Future protocols: WebSockets and WebRTC

Neither is built by this spec. These notes are here to show the seam holds.

**WebSockets (`websocket`)**

- Same `path`. `Accepts` is `context.WebSockets.IsWebSocketRequest`.
- Text frames carry `{"id","event","data"}`. Pings use `WebSocketOptions.KeepAliveInterval`.
- Resume with `?last_event_id=`.
- **The first protocol to use `Inbound`.** Client messages publish to the stream's `to` target
  (`to: rabbit:chat.messages`), through that broker's publish workers, exactly like a webhook's.
  Each message is wrapped in an envelope with the stream ID, client ID, and the authenticated
  user, and limited by size and rate per client.

**WebRTC data channels (`webrtc`)**

- Signaling is `POST {path}/webrtc` with an SDP offer, answered with SDP. It's one round trip,
  WHEP-style, without trickle ICE.
- ICE servers come from `protocols.webrtc.ice_servers` (STUN, TURN).
- One data channel per subscriber, carrying the same JSON frames as WebSockets.
- Worth it only for unordered or unreliable "latest value wins" feeds, or for clients already
  in a WebRTC session. For ordered event feeds, WebSockets or SSE are simpler and as fast.
- Needs a WebRTC stack such as SIPSorcery. That dependency is heavy enough to justify the first
  package beside the core, `Runax.Hookpipe.Streaming.WebRtc`, so library users who don't need
  WebRTC never pull it in. It plugs in through the public `IStreamProtocol`.

**Not SignalR.** SignalR brings its own protocol and requires its client library, while SSE
works with the browser's built-in `EventSource`. A SignalR adapter could be added later behind
the same seam.

## 4.9 Improvement: local delivery, dedupe, and debounce

This isn't part of the first SSE release. It's Phase 6 ([§10](implementation-plan.md#10-plan)),
and the stream core is designed so it slots in without changes.

### Why

A webhook publishes event C to `rabbit:orders`, and a stream reads `rabbit:orders`. Without
local delivery, C goes out to the broker and back before a client on the same replica sees it.
That's two network hops plus broker queueing: usually milliseconds, much more under load (Kafka
linger, SQS long polling). Meanwhile the replica already has C in memory.

### How it works

```
PublishWorker ── broker confirms C ──► local tap ──► stream inbox   (C, source: local)
broker ─────────── delivers C later ───────────────► stream inbox   (C, source: broker)
StreamWorker: the first copy wins; a later copy with the same message ID is dropped
```

- **Matching.** At startup, Hookpipe compares each stream's `from` targets with the brokers that
  webhooks publish to. An item whose broker and rendered topic equal one of a stream's `from`
  targets is offered to that stream. Topics must match exactly; wildcards come with P3.
- **The same shape as the broker's copy.** The local copy is built the way the consumer would
  deliver it:
  - the body is the envelope's serialized JSON, the same `JsonElement` as in
    [§6.1](messaging-integration.md#61-wiring);
  - the headers are the native ones (`hookpipe.message.id`, `hookpipe.webhook.id`,
    `traceparent`).

  Filters, event names, and `event.data` therefore behave the same for both copies.
- **When it's offered.**
  - `local_delivery: after_publish` (the default) offers it once the broker confirms the publish,
    so clients never see an event that failed to publish.
  - `on_accept` offers it as soon as the webhook's item is queued. It's the fastest, but a client
    can see an event that later fails to publish. Use it for previews and webhook inspectors.
  - `off` turns it off.
- **It never slows publishing.** The tap uses `TryWrite`. If the stream's inbox is full, the
  local copy is skipped, and the broker's copy delivers the event a little later. Nothing is
  lost.
- **Other replicas are unaffected.** They receive the event from the broker as before, through
  their broadcast subscription.
- **Ordering.** On the publishing replica, a local event can overtake events from other replicas
  that are still travelling through the broker. Each replica's stream already has its own order
  (IDs are per replica), so this only changes which replica sees what first.

### Dedupe

Dedupe drops a second copy of the *same* message. Local delivery needs it, and it also absorbs
broker redeliveries and producers that publish twice.

- **Key.** `dedupe.key` is a template, by default `{header.hookpipe.message.id}` (the envelope
  ID). Messages from other producers don't carry that header and pass through untouched. A
  foreign producer's own ID works too, for example `dedupe.key: "{$.event_id}"`.
- **Window.** The worker remembers keys it has seen for `dedupe.window` (default `2m`), up to
  `dedupe.max_entries` (default 100,000), evicting the oldest first. At the cap that's on the
  order of 10 MB per stream.
- **Late copies.** A copy that arrives after its key has left the window is delivered again, as a
  duplicate. Size the window above the broker's worst normal delivery delay.
- **Before IDs are assigned.** Dedupe runs in the stream worker before an event gets its ID, so a
  dropped copy never uses a sequence number or a replay slot.
- **When it's on.** Dedupe is always on while local delivery is on. It can also be turned on by
  itself, just by setting `dedupe.key`.

### Debounce

Debounce is different from dedupe. It merges *different* messages about the same thing, so a
client gets the latest state instead of every intermediate one.

```yaml
debounce:
  key: "{$.body.order.id}"     # events with the same key are merged
  window: 250ms
  emit: latest                 # latest | first
```

- **`emit: latest`** (trailing). The first event for a key starts the window. Later events with
  the same key replace the held one. When the window closes, the held event is sent.
- **`emit: first`** (leading). The first event is sent at once, and the rest for that key are
  dropped until the window closes.
- **The single-owner rule still holds.** The worker keeps a map of pending keys, and a timer
  channel it reads alongside control and inbox, so no second loop touches stream state.
  `debounce.max_keys` (default 10,000) bounds the map. Beyond it, events pass through
  undelayed, and a metric counts them.
- **IDs are assigned when an event is sent,** so the replay buffer holds exactly what clients
  saw.
- **Use it for dashboards and status feeds** where only the latest state matters. Not for event
  logs where every event counts.
- **It's per stream** and applies to all subscribers. Debounce per subscriber can come later.

### Configuration

These keys go on `streams.<id>` ([§5.8](configuration.md#58-streams)):

| Key | Description | Default |
| --- | --- | --- |
| `local_delivery` | `after_publish`, `on_accept`, or `off` | `after_publish` |
| `dedupe.key` | Template for the dedupe key | `{header.hookpipe.message.id}` |
| `dedupe.window` | How long keys are remembered | `2m` |
| `dedupe.max_entries` | Cap on remembered keys | `100000` |
| `debounce.key` | Template for the debounce key; turns debounce on | none |
| `debounce.window` | Merge window | `250ms` |
| `debounce.emit` | `latest` or `first` | `latest` |
| `debounce.max_keys` | Cap on keys waiting | `10000` |
