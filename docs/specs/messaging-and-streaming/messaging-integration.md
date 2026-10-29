# 6. runax-messaging integration

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 6.1 Wiring

The library's `AddRunaxHookpipe` does the wiring. The service's `Program.cs` only calls it
([§11.6](packaging.md#116-the-self-hosted-service)). Inside:

```csharp
var definition = builder.Build();                             // YAML, IConfiguration, and code
var streams = new StreamRegistry(definition.Streams, …);      // inboxes exist before any consumer runs

services.AddSingleton(streams);
services.AddHostedService<StreamWorkerHost>();                // order relative to consumers doesn't matter
services.AddRunaxMessaging(messaging =>
{
    foreach (var broker in definition.HookpipeOwnedBrokers)
        messaging.AddHookpipeBroker(broker);                  // AddBus("hookpipe:{id}"): transport + mode

    foreach (var (busName, stream, topic) in definition.Subscriptions)   // Hookpipe-owned and app-owned
        messaging.AddConsumer(busName, topic, (context, ct) => streams[stream].OfferAsync(context, ct));  // P6
});

services.AddHostedService<PublishWorkerHost>();               // publish workers per broker, supervised
```

**One extension call per broker, one line per subscription.**

- `AddHookpipeBroker` is a public extension on `MessagingConfigurator` in the library. It wraps
  `AddBus` for a Hookpipe-owned broker: the `hookpipe:{id}` name, the transport config built from
  `settings`, and the mode derived from how the broker is used. An app that writes its own
  `AddRunaxMessaging` block can call it there too.
- Subscriptions don't go inside a bus block any more. P6's
  `messaging.AddConsumer(busName, topic, handler)` registers them the same way on Hookpipe's buses
  and on the app's, so there's a single code path. It builds on P1's delegate consumer.

**Each envelope is serialized once, by Hookpipe.** The webhook handler serializes the envelope
with Hookpipe's own options (camelCase, nulls omitted, v1's wire shape) and hands every target
the resulting `JsonElement`. runax-messaging's System.Text.Json serializer writes a `JsonElement`
verbatim, whatever naming policy the bus uses. Without this, its default PascalCase options
would turn `webhookId` into `WebhookId`. The approach keeps the wire shape right on
app-owned buses Hookpipe can't configure ([§11.5](packaging.md#115-brokers-in-library-mode)),
and serializes once per webhook instead of once per target.

**No hosted-service ordering.** Stream inboxes are created with the registry, before any hosted
service starts, so Hookpipe doesn't depend on registration order. See
[§11.5](packaging.md#115-brokers-in-library-mode).

## 6.2 Hookpipe-local transports

`stdout`, `http`, and `eventbridge` have no runax-messaging transport today. Each becomes a
`TransportConfig` + `IMessagingTransport` pair. `stdout` and `http` live in the library
(`Runax.Hookpipe/Brokers/Transports`). `eventbridge` lives in the service for now, so the library
doesn't pull in the AWS SDK. Each is written as runax-messaging's [custom transport guide](https://github.com/runax-software/runax-messaging/blob/main/docs/writing-a-custom-transport.md)
describes. They're publish-only: `SubscribeAsync` throws, and the loader keeps streams off them.

- **`http`** POSTs the body and sends `TransportMessage.Headers` as HTTP headers. Non-2xx
  responses throw, so the broker's `publish.retry` applies, as in v1.
- **`eventbridge`** moves into runax-messaging as `Runax.Messaging.Transports.Aws.EventBridge`
  (P8). When that ships:
  - the service deletes its own copy and registers the package's config type;
  - library users get EventBridge by adding the package and calling `AddBrokerType`;
  - `type: eventbridge` keeps working in both modes.

## 6.3 Wire changes for downstream consumers

| What | v1 | v2 |
| --- | --- | --- |
| Body | Hookpipe envelope JSON | Same casing, because Hookpipe serializes it itself (§6.1). One field is renamed (next row). |
| Webhook ID field | `endpointId` | `webhookId` |
| Extra key | — | `__runax` (`headers`, trace context). runax-messaging consumers strip it. Strict JSON readers that reject unknown properties must allow it. |
| Message ID / webhook ID headers | `hookpipe.message.id`, `hookpipe.endpoint.id` (Kafka headers, SQS attributes) | `hookpipe.message.id` and `hookpipe.webhook.id`, as native headers on every transport that has a header model, and in `__runax.headers` |
| RabbitMQ exchange | Per sink (`exchange`), declared topic and durable | Per broker (`exchange_name`), declared topic and durable by the transport. Same declaration. |
| RabbitMQ `MessageId` property | Envelope ID | Not set; use the `hookpipe.message.id` header |
| RabbitMQ delivery mode, content type | Persistent, `application/json` | Same |
| Kafka key | Endpoint ID | None until P2. Ordering per webhook is lost in the meantime. |
| Trace context | — | `traceparent` / `tracestate` native headers, continuing the webhook request's trace |

Downstream consumers must update for `webhookId` and `hookpipe.webhook.id` when they move to v2.
Both are listed in [§13](breaking-changes.md). The Kafka key gap blocks Phase 4 for Kafka users
unless P2 ships first.

## 6.4 Health

The library adds its checks to the host's `AddHealthChecks()`, tagged `hookpipe`,
`hookpipe-brokers`, and `hookpipe-streams`. `MapHookpipeHealth()` maps the JSON endpoints below.

- **`/health`** is liveness and doesn't change. The service maps it, not the library.
- **`/health/ready`** is new. It checks every broker with runax-messaging's auto-registered
  `runax:{bus}` health check (`runax:hookpipe:{broker}` for buses Hookpipe owns), plus whether
  every publish worker and stream worker is running.
- **`/health/brokers`** replaces v1's `/health/sinks`, which is gone. Per broker it reports:
  - the probe status;
  - for each publish worker, its `queue_depth`, `queue_capacity`, and state (`running`,
    `restarting`, or `stopped`);
  - for brokers that streams read from, the subscription.
- **`/health/streams`** is new. Per stream it reports broker status, worker state, and client
  count per protocol.
- **All probes are live.** They run on each request and are never cached. Each one keeps v1's
  3 s timeout (`BrokerHealthEndpoints.ProbeTimeout`).

## 6.5 Telemetry

runax-messaging emits publish and consume spans and metrics on `Runax.Messaging`
(`ActivitySource` and `Meter`), tagged `messaging.runax.bus` with the bus name. Hookpipe's own
metrics and spans move to a `Meter` and an `ActivitySource` named `Runax.Hookpipe`
([§11.3](packaging.md#113-projects-and-packages)). Library users export both with whatever their
app uses. The service exports them through prometheus-net's `MeterAdapter`.

**v1 metrics are renamed** so they match the new terms. Dashboards need updating:

| v1 | v2 |
| --- | --- |
| `hookpipe_requests_total{endpoint_id, method, status_code}` | `hookpipe_requests_total{webhook_id, method, status_code}` |
| `hookpipe_messages_produced_total{endpoint_id, sink_id}` | `hookpipe_messages_published_total{webhook_id, broker_id}` |
| `hookpipe_sink_errors_total{endpoint_id, sink_id}` | `hookpipe_publish_errors_total{webhook_id, broker_id}` |
| `hookpipe_validation_failures_total{endpoint_id, validator_type}` | `hookpipe_validation_failures_total{webhook_id, validator_type}` |
| `hookpipe_request_duration_seconds{endpoint_id}` | `hookpipe_request_duration_seconds{webhook_id}` |

Topics aren't a label anywhere: body-derived topics would make the label set unbounded.

**New metrics:**

| Metric | Type | Labels |
| --- | --- | --- |
| `hookpipe_publish_queue_depth` | gauge | `broker_id`, `worker` |
| `hookpipe_publish_queue_capacity` | gauge | `broker_id`, `worker` |
| `hookpipe_publish_enqueue_rejected_total` | counter | `broker_id`, `reason` (`full`, `timeout`, `stopping`) |
| `hookpipe_publish_abandoned_total` | counter | `broker_id` |
| `hookpipe_publish_batch_size` | histogram | `broker_id` |
| `hookpipe_publish_duration_seconds` | histogram | `broker_id` |
| `hookpipe_publish_worker_up` | gauge | `broker_id`, `worker` |
| `hookpipe_template_failures_total` | counter | `broker_id`, `reason` (`template_unresolved`, `template_invalid`) |
| `hookpipe_delivery_wait_seconds` | histogram | `webhook_id` |
| `hookpipe_stream_clients` | gauge | `stream_id`, `protocol` |
| `hookpipe_stream_events_received_total` | counter | `stream_id`, `source` (`broker`, `local`) |
| `hookpipe_stream_events_sent_total` | counter | `stream_id`, `protocol` |
| `hookpipe_stream_events_dropped_total` | counter | `stream_id`, `reason` (`filtered`, `slow_client`, `duplicate`, `debounced`) |
| `hookpipe_stream_local_skipped_total` | counter | `stream_id` (local copy skipped because the inbox was full) |
| `hookpipe_stream_disconnects_total` | counter | `stream_id`, `protocol`, `reason` (`client`, `slow_client`, `max_age`, `shutdown`) |
| `hookpipe_stream_resumes_total` | counter | `stream_id`, `result` (`replayed`, `gap`) |

**New log prefixes**, following [AGENTS.md](../../../AGENTS.md):

- `[Hookpipe.Webhook:{id}]` replaces `[Hookpipe.Endpoint:{id}]`.
- `[Hookpipe.Broker:{type}:{id}]` for the bus.
- `[Hookpipe.Broker:{id}:worker-{n}]` for a publish worker.
- `[Hookpipe.Stream:{id}]` and `[Hookpipe.Stream:{id}:{protocol}]`.
