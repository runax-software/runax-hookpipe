# Implementation plan

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 8. Code layout

There are still two production projects, as AGENTS.md requires. They're renamed for the
package ([§11.3](packaging.md#113-projects-and-packages)). The engine lives in the library:

```
src/Runax.Hookpipe/
  Hosting/             AddHookpipe, HookpipeBuilder (YAML + code), MapHookpipe*, MapHookpipeHealth
  Config/              v2 models, loader (strict), interpolation, hot-reload diff
    Sources/           YamlSource, ConfigurationSource (IConfiguration), CodeSource: each builds
                       the same node tree, which one binder turns into the model
  Brokers/             BrokerTypeRegistry (type → TransportConfig), BrokerUsage (bus mode), bus wiring
    Transports/        StdoutTransport, HttpRelayTransport (+ configs)
  Publishing/          PublishWorker, PublishItem, PublishOutcome, PublishWorkerHost, BrokerPublisher,
                       LocalDeliveryTap (Phase 6)
  Webhooks/            WebhookHandler (was Services/WebhookHandler), RouteEvaluator, EnvelopeBuilder
  Conditions/          Condition model and evaluator (route.when, stream filters), JSONPath
                       compilation (JsonPath.Net), templates and their renderer
  Streams/             StreamRegistry, StreamWorker, StreamWorkerHost, StreamSubscription,
                       StreamFrame, ReplayBuffer, StreamEndpoint (auth, CORS, protocol selection),
                       DedupeWindow, Debouncer (Phase 6)
    Protocols/         IStreamProtocol
      Sse/             SseProtocol, SseFrameWriter
  Validation/          Validators (unchanged logic, list form, secrets resolved at load)
  Health/              Health checks, readiness, the /health/brokers and /health/streams JSON
  Diagnostics/         HookpipeDiagnostics: the Runax.Hookpipe Meter and ActivitySource
```

The service only hosts it:

```
src/Runax.Hookpipe.Api/
  Program.cs           ~20 lines (§11.6)
  ServiceDefaults.cs   Serilog (console, Seq, Loki), Kestrel body limit, config path
  BrokerTypes.cs       AddAllBrokerTypes: the eight runax-messaging transports + eventbridge
  Transports/          EventBridgeTransport (+ config), deleted once P8 ships
```

**Removed:**

- The 11 `Sinks/*Sink.cs` files, `ISink`, `SinkFactory`, and `SinkHelper`.
- `Sinks/Health/ISinkHealthCheck`, replaced by runax-messaging health checks plus worker state.
- prometheus-net's static counters in the library. prometheus-net stays, in the service only.

**Packages:**

- Removed from `Directory.Packages.props`: `RabbitMQ.Client`, `Confluent.Kafka`, `AWSSDK.SQS`,
  `AWSSDK.SimpleNotificationService`, `StackExchange.Redis`, `Google.Cloud.PubSub.V1`,
  `Azure.Messaging.ServiceBus`, and `Azure.Messaging.EventHubs`. They come in transitively,
  pinned by the transports.
- The library references `Runax.Messaging`, `Polly.Core`, `YamlDotNet`, and `JsonPath.Net`.
- The service references the library, the eight `Runax.Messaging.Transports.*` packages,
  `AWSSDK.EventBridge`, prometheus-net, and Serilog.
- Added for packaging: `Microsoft.CodeAnalysis.PublicApiAnalyzers`.

**Docs to update:**

- AGENTS.md: it still says .NET 8. It needs the new project names, and its "adding a sink" steps
  become "adding a broker type" and "adding a stream protocol".
- CONTRIBUTING.md and the root README ([§11.9](packaging.md#119-docs)).
- `docs/configuration.md`, and a new `docs/upgrading.md`: the v1 → v2 guide the startup error links
  to ([§5.10](configuration.md#510-upgrading-from-v1)).
- `docs/sinks.md`, which becomes `docs/brokers.md`.
- The new `docs/streams.md` and `docs/library.md`.

## 9. Failure modes

| Situation | Behavior |
| --- | --- |
| Broker unreachable at startup | Hookpipe warms every bus at startup and fails fast, as v1 does today. A broker marked `optional: true` starts degraded instead: readiness returns 503 and webhooks that target it get 503 for that target. |
| Broker fails at runtime | Its workers retry per policy and their queues fill. Enqueues then wait and return 503, and readiness returns 503. Other brokers are unaffected. |
| A worker loop crashes | Its in-flight items fail, the loop restarts with backoff, and an error is logged. `hookpipe_publish_worker_up` drops to 0 until it's back. |
| Stream inbox full | Consumption slows through broker flow control. Nothing is dropped. |
| Client too slow | Per `slow_client`. The client reconnects and resumes from the ring. |
| Process crash | Queued publish items are lost (in memory). With `ack: delivered`, their callers got no 2xx and will retry. With `ack: enqueued` they're gone. That's the trade-off the mode states. |
| Invalid config on reload | The previous config stays in force, as in v1, and the diff is logged. |

## 10. Plan

Each phase ships on its own.

| Phase | Scope | Behavior change |
| --- | --- | --- |
| 0 | runax-messaging P1, P2, and P6 (minor release) | None in Hookpipe |
| 1 | Rename and split ([§11](packaging.md)): `Runax.Hookpipe` + `Runax.Hookpipe.Api`, the service on the public API only, metrics on `System.Diagnostics.Metrics`, Dockerfile on .NET 10, image published under the new name, with the old package's `0.2.x` tags copied over and the old package deleted. No NuGet release yet. | New image name, logger categories |
| 2 | Config v2: strict loader, interpolation, `validate` lists, `route`, code-first definitions, the `IConfiguration` source. Still on `ISink`. | v1 files are rejected at startup; startup also fails on typos and missing secrets |
| 3 | Publish workers per broker, still on the existing `ISink` classes (each target is served by an `ISink` built from its broker and topic): queues, `workers` / `order_by`, `ack` modes, batching hooks, supervision, new metrics, `/health/ready` | Ordering per broker or per key; 503 and 504 become possible |
| 4 | Replace `ISink` with buses: Hookpipe-owned and app-owned buses, local transports, envelope serialized once, wire changes ([§6.3](messaging-integration.md#63-wire-changes-for-downstream-consumers)), SDK code removed. First NuGet release of `Runax.Hookpipe` (0.x). | Wire changes in §6.3 |
| 5 | Streams + SSE: registry, worker, replay, filters, auth, CORS, `/health/streams`, docs | New feature |
| 6 (improvement) | Local delivery with dedupe and debounce ([§4.9](streaming.md#49-improvement-local-delivery-dedupe-and-debounce)) | Lower latency on the publishing replica |
| 7 (later) | WebSockets, then inbound publish through stream `to` targets | — |
| 8 (later) | WebRTC data channels, in a separate `Runax.Hookpipe.Streaming.WebRtc` package | — |

**Why this order:**

- **Phase 1 goes first** so every later phase lands in its final project and namespace once.
  It's also where the broken Docker build gets fixed.
- **Phase 3 comes before Phase 4.** The new execution model and the library swap land
  separately, so a regression points at one of them.

**Tests** go beside `src/`:

- `tests/Runax.Hookpipe.Tests` covers the library, on a minimal test host. It also covers both
  three ways of defining things (YAML, `IConfiguration`, code) and app-owned buses. The same
  definition loaded from YAML and from `appsettings.json` must produce an identical model.
- `tests/Runax.Hookpipe.Api.Tests` covers the service: `WebApplicationFactory<Program>`, the
  exported `/metrics` names, and the config path.

**Unit tests:**

- Loader: strict keys, interpolation, references, and the hot-reload diff.
- v1 files are rejected with an error that names the file and links the upgrade guide.
- `PublishWorker` with runax-messaging's TestKit (`MessagingTestHarness`): ordering under retry,
  batching, abandoned items, `when_full`, `order_by` routing, drain on shutdown.
- Conditions: every operator, typed and text equality, several selected values, `all`/`any`/`not`,
  JSONPath compile errors at load, templates with defaults, `template_invalid` topics.
- Local delivery (Phase 6): the first copy wins, the dedupe window, a full inbox skipping the
  local copy, both debounce modes.
- `StreamWorker`: sequence numbers, atomic subscribe and replay, gaps, both slow-client
  policies, path-parameter filters.
- `SseFrameWriter`: multi-line data, comments, `retry`.

**Integration tests:**

- The existing docker compose brokers, through the new path.
- Webhook → RabbitMQ → SSE end to end.
- Two Hookpipe instances on Kafka, checking that both SSE clients receive every event.

**Load tests:**

- Ingress throughput per broker, with and without batching and extra workers.
- SSE fan-out to N clients, with one deliberately slow.
