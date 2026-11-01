# Next pull requests

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

This file splits the remaining phases of [§10](implementation-plan.md#10-plan) into pull requests.
Each one builds, passes CI, and ships on its own. Update it as PRs land. The splits for Phase 4
and later are rough until Phase 3 is in.

## Done

| PR | Phase | Scope |
| --- | --- | --- |
| [#1](https://github.com/runax-software/runax-hookpipe/pull/1) | — | The spec |
| [#2](https://github.com/runax-software/runax-hookpipe/pull/2) | 1 | Rename to `Runax.Hookpipe`, split the library from the service |
| [#3](https://github.com/runax-software/runax-hookpipe/pull/3) | 2, PR A | Config v2: strict loader, interpolation, brokers and targets, `validate` lists, `route` conditions. Sinks become brokers, `Config` becomes `Configuration`, and the library registers with `AddRunaxHookpipe`. |
| [#4](https://github.com/runax-software/runax-hookpipe/pull/4) | 2, PR B | Templates in topics, `message.metadata`, and condition values |

## Order

| # | Pull request | Phase | Needs | Size | Behavior change |
| --- | --- | --- | --- | --- | --- |
| 1 | [FluentAssertions → Shouldly](#1-fluentassertions--shouldly) | — | — | S | None (tests only) |
| 2 | [PR C: the `IConfiguration` source](#2-pr-c-the-iconfiguration-source) | 2 | — | M | New feature |
| 3 | [PR D: definitions in code](#3-pr-d-definitions-in-code) | 2 | 2 | M | New feature |
| 4 | [PR E: `max_body_size`](#4-pr-e-max_body_size) | 2 | — | S | `HOOKPIPE_MAX_BODY_SIZE_MB` is removed |
| 5 | [Publish workers and delivery modes](#5-publish-workers-and-delivery-modes) | 3 | — | L | Per-broker ordering; 503 and 504 become possible |
| 6 | [More workers and `order_by`](#6-more-workers-and-order_by) | 3 | 5 | M | New keys |
| 7 | [Batching](#7-batching) | 3 | 5 | M | New keys |
| 8 | [Readiness and worker health](#8-readiness-and-worker-health) | 3 | 5 | S | New `/health/ready` |
| — | [runax-messaging P1, P2, P6](#runax-messaging-phase-0) | 0 | — | M | None in Hookpipe |
| 9 | [Brokers on runax-messaging buses](#9-brokers-on-runax-messaging-buses) | 4 | 5, P2 | L | Wire changes ([§6.3](messaging-integration.md#63-wire-changes-for-downstream-consumers)) |
| 10 | [Remove `IBroker` and the broker SDKs](#10-remove-ibroker-and-the-broker-sdks) | 4 | 9 | M | None |
| 11 | [First NuGet release](#11-first-nuget-release) | 4 | 10 | M | First package, 0.x |
| 12 | [Stream core](#12-stream-core) | 5 | 9, P1, P6 | L | New feature |
| 13 | [SSE and stream endpoints](#13-sse-and-stream-endpoints) | 5 | 12 | M | New feature |
| 14 | [Stream health, metrics, and end-to-end tests](#14-stream-health-metrics-and-end-to-end-tests) | 5 | 13 | M | New `/health/streams` |
| 15 | [Local delivery](#later) | 6 | 14 | M | Lower latency |

**Why this order:**

- **Shouldly first.** Every later PR adds tests. Landing the swap now means nothing has to be
  converted twice.
- **PR E doesn't depend on C or D.** It can go in any time before the first release.
- **runax-messaging's Phase 0 runs alongside Phases 2 and 3.** It's another repository, and nothing
  here needs it before Phase 4.
- **Phase 3 runs on the existing `IBroker` classes,** as §10 plans. The new execution model and
  the library swap land separately, so a regression points at one of them.

## Phase 2: finish config v2

### 1. FluentAssertions → Shouldly

FluentAssertions 8 and later needs a paid license for commercial use. runax-messaging already uses
Shouldly.

- **The work is mostly done.** It's in the worktree `../runax-hookpipe-shouldly` on branch
  `test/shouldly`, uncommitted, and three commits behind `main`.
- **Rebase onto `main`, then convert what PR B added:**
  - the new `Conditions/TemplateTests.cs` and `Webhooks/WebhookTemplateTests.cs`;
  - PR B's new tests in `CompareConditionTests.cs`, `ConfigurationLoaderTests.cs`,
    `EnvelopeBuilderTests.cs`, and `ApiMetricsTests.cs`.
- **Already on the branch:** the package swap in `Directory.Packages.props`, plus AGENTS.md,
  CONTRIBUTING.md, and §8 of the plan.

### 2. PR C: the `IConfiguration` source

Spec: [§5.12](configuration.md#512-appsettingsjson-and-other-iconfiguration-sources).

- **`hookpipe.LoadConfiguration(section)`**, an extension method on `HookpipeConfigurator` next to
  `LoadYaml`.
- **A source under `Configuration/Sources/`** that turns an `IConfigurationSection` into the same
  node tree `YamlSource` builds, so the one binder and every load-time check apply. Its name
  mustn't clash with `Microsoft.Extensions.Configuration` types.
- **How the section is read:**
  - Keys match ignoring case and underscores.
  - A section whose children are `0`, `1`, `2`, … is a list.
  - `Version` may be left out.
  - Values are untyped text, handled as §5.12 describes.
- **Errors** name the configuration path (`Hookpipe:Webhooks:github:Validate:0:Secrett`), never the
  value.
- **Reload** follows the section's change token, with the same rules as the YAML watcher
  ([§5.11](configuration.md#511-hot-reload)).
- **Combining sources:**
  - Each broker and webhook comes from exactly one source; the same ID in two is a load error.
  - Setting the same `server` key in two sources is an error.
- **Tests:** the same definition loaded from YAML and from `appsettings.json` produces an identical
  model.
- **Docs:** a section in `docs/configuration.md`.

### 3. PR D: definitions in code

Spec: [§11.4](packaging.md#114-the-library-api).

- **Webhooks:** `hookpipe.AddWebhook(id, webhook => …)`, with validators, `to`, `route`,
  `message`, and `rate_limit`. Conditions are built with `Condition.JsonPath(…)`,
  `Condition.Header(…)`, and the rest.
- **Brokers:** brokers defined in code, and `ConfigureBroker(id, …)` for publish settings.
- **Paths:** `hookpipe.Paths` instead of `server.paths`.
- **Validators:** `AddValidator<T>(type)` makes custom validators an extension point, and
  `IValidator` becomes the public `IWebhookValidator`.
- **One model:** code goes through a source that builds the same model and the same load-time
  checks as YAML. It's fixed for the life of the process, and IDs must be unique across all
  sources.
- **Later phases:** `AddStream` waits for Phase 5, and `UseBus` and `AddBrokerType` wait for
  Phase 4.

### 4. PR E: `max_body_size`

Spec: [§5.3](configuration.md#53-server), [§5.6](configuration.md#56-webhooks),
[§11.5](packaging.md#115-brokers-in-library-mode).

- **`server.max_body_size`** (`25MB`) sets Kestrel's limit in the service. The library ignores it
  with a warning.
- **`webhooks.<id>.max_body_size`** is applied per request through
  `IHttpMaxRequestBodySizeFeature`, returns 413 above the limit, and changes live on reload.
- **Removed:** the `HOOKPIPE_MAX_BODY_SIZE_MB` environment variable. Add a row to
  `docs/upgrading.md`; §13 already lists the change.

## Phase 3: publish workers, still on `IBroker`

### 5. Publish workers and delivery modes

Spec: [§3.3](architecture.md#33-publish-workers-one-per-broker-by-default),
[§3.4](architecture.md#34-ingress-http--brokers), [§3.5](architecture.md#35-publish-worker).

- **One `PublishWorker` per broker:**
  - each worker has a bounded `Channel<PublishItem>`;
  - `PublishWorkerHost` runs the workers;
  - `BrokerPublisher` holds the settings a reload can change;
  - the new types live in `Publishing/`.
- **Enqueueing and publishing:**
  - The handler renders each target and enqueues it.
  - The worker publishes through the broker's existing `IBroker`, with the target's topic.
  - Retry moves from the handler into the worker.
- **Queue settings:** `publish.queue.capacity`, `when_full` (`wait` or `reject`), and
  `enqueue_timeout`.
- **`webhooks.<id>.delivery`:** `ack` (`delivered` or `enqueued`), `accept_when` (`any` or `all`),
  and `timeout`.
- **New responses:**
  - 503 `broker_unavailable`, with `Retry-After`;
  - 504 `delivery_timeout`.
  - Abandoned items are skipped.
- **Items outlive the request:**
  - Items never capture `RequestAborted`.
  - Each item carries the request's `ActivityContext`, so the publish span links back to the
    webhook's trace.
- **Supervision:** a faulted loop fails its in-flight items and restarts with backoff.
- **Shutdown:** `server.shutdown_timeout` drains the queues.
- **New metrics:**
  - `hookpipe_publish_queue_depth` and `hookpipe_publish_queue_capacity`;
  - `hookpipe_publish_enqueue_rejected_total` and `hookpipe_publish_abandoned_total`;
  - `hookpipe_publish_duration_seconds` and `hookpipe_publish_worker_up`;
  - `hookpipe_delivery_wait_seconds`.
- **Tests:**
  - ordering under retry;
  - abandoned items;
  - both `when_full` modes;
  - draining on shutdown;
  - the 503 and 504 responses.

### 6. More workers and `order_by`

- **`publish.workers: N`.**
- **`publish.order_by`:**
  - a template whose default is `{topic}`;
  - `none` sends each item to the least-loaded worker.
- **Per-target `order_by`** in the object target form. Today the binder reports it as an unknown
  key.
- **`{topic}`** becomes usable in `order_by`; elsewhere it stays reserved.
- **Tests:** the same key always lands on the same worker, and per-key order holds while other keys
  publish in parallel.

### 7. Batching

- **Settings:** `publish.batch.max_size` and `max_delay`.
- **How a batch is built:**
  - it's split by rendered topic;
  - abandoned items are dropped before publishing.
- **Publishing a batch:**
  - `IBroker` gets a batch publish, which falls back to one at a time by default;
  - SQS uses `SendMessageBatch`, and RabbitMQ needs one confirm round trip per batch.
- **New metric:** `hookpipe_publish_batch_size`.

### 8. Readiness and worker health

Spec: [§6.4](messaging-integration.md#64-health), [§9](implementation-plan.md#9-failure-modes).

- **`/health/ready`:**
  - every broker's probe, plus every publish worker running;
  - 503 otherwise.
- **`/health/brokers`** adds each worker's `queue_depth`, `queue_capacity`, and state (`running`,
  `restarting`, or `stopped`).
- **Probes stay live:**
  - they run on each request, never from a cache;
  - each has a 3 s timeout.
- **`optional: true` brokers** start degraded instead of failing startup (§9). §5.4's key table
  doesn't list `optional` yet; add it there in this PR.

## runax-messaging (Phase 0)

This is in the runax-messaging repository and ships as a minor release. Spec:
[§7](messaging-prerequisites.md#7-prerequisite-changes-in-runax-messaging).

| Change | Blocks |
| --- | --- |
| **P1:** delegate consumers on runtime topics | Phase 5 |
| **P2:** the `runax-partition-key` header, mapped by each transport | Phase 4, for Kafka users |
| **P6:** consumers on an existing bus, added outside its `AddBus` block | Phase 5 |

P8, the EventBridge transport, is optional. Until it ships, the service keeps its own `eventbridge`
transport.

## Phase 4: runax-messaging buses

### 9. Brokers on runax-messaging buses

Spec: [§6.1](messaging-integration.md#61-wiring), [§6.2](messaging-integration.md#62-hookpipe-local-transports),
[§11.5](packaging.md#115-brokers-in-library-mode).

- **Broker types:**
  - `AddBrokerType<TConfig>(type)`;
  - the service registers all of them through `AddAllBrokerTypes`;
  - the library registers only `stdout` and `http`.
- **One bus per broker:**
  - each broker with a `type` becomes the bus `hookpipe:{id}`, with its `BusMode` derived from how
    it's used;
  - `settings` bind into the transport's `TransportConfig`, and its validation errors carry the
    config path.
- **App-owned buses:** `bus:` in config and `UseBus` in code.
- **Hookpipe-local transports:** `stdout`, `http`, and `eventbridge`.
- **Publishing:**
  - workers publish through `IBus`;
  - the envelope is serialized once;
  - `partition_key` is accepted (P2).
- **Wire changes** for downstream consumers, as §6.3 lists them.

### 10. Remove `IBroker` and the broker SDKs

- **Deleted code:**
  - the broker folders, `IBroker`, `BrokerFactory`, and `BrokerHelper`;
  - `IBrokerHealthCheck`, replaced by runax-messaging's health checks plus worker state.
- **Packages:**
  - the eight broker SDKs leave `Directory.Packages.props`;
  - prometheus-net moves to the service only.
- **Docs:** `docs/brokers.md` and AGENTS.md ("adding a broker type").

### 11. First NuGet release

Spec: [§11.8](packaging.md#118-releases), [§11.9](packaging.md#119-docs).

- **Public API tracking:** `Microsoft.CodeAnalysis.PublicApiAnalyzers`, with
  `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`.
- **Package docs:**
  - the package README;
  - the root README split into "Run as a service" and "Use as a library";
  - `samples/Runax.Hookpipe.Samples.Embedded`.
- **Release workflow:**
  - one GitHub release publishes the package and the image;
  - NuGet Trusted Publishing, symbol packages, and SourceLink.
- **Version:** 0.x.

## Phase 5: streams and SSE

### 12. Stream core

Spec: [§4.3](streaming.md#43-stream-worker), [§4.5](streaming.md#45-server-side-filters),
[§5.8](configuration.md#58-streams), [§3.7](architecture.md#37-every-replica-must-see-every-message).

- **Configuration:**
  - the `streams` section, and `AddStream` in code;
  - `server.instance_id` and `subscribe.mode`.
- **New types:** `StreamRegistry`, `StreamWorker`, the replay buffer, and `StreamSubscription`.
- **Subscriptions** use P1 and P6, with a group per replica for broadcast.
- **Filters**, including path parameters.

### 13. SSE and stream endpoints

Spec: [§4.2](streaming.md#42-protocol-seam), [§4.6](streaming.md#46-sse-adapter),
[§4.7](streaming.md#47-shutdown).

- **The protocol:** `IStreamProtocol`, `SseProtocol`, and `SseFrameWriter`.
- **The endpoint:** `StreamEndpoint`, with auth and CORS.
- **Routing:**
  - `server.paths.streams`;
  - `MapHookpipeStreams()`, and `MapHookpipeWebhooks()` so each can get its own policies.
- **Clients:**
  - the slow-client policies;
  - resuming from the replay buffer;
  - shutdown.

### 14. Stream health, metrics, and end-to-end tests

- **Health and metrics:**
  - `/health/streams`;
  - the `hookpipe_stream_*` metrics.
- **Docs:** `docs/streams.md` and `docs/library.md`.
- **End-to-end tests:**
  - webhook → RabbitMQ → SSE;
  - two Hookpipe instances on Kafka, where both SSE clients receive every event.

## Later

| PR | Phase | Spec |
| --- | --- | --- |
| **15. Local delivery:** the publishing replica feeds its own streams, with dedupe and debounce | 6 | [§4.9](streaming.md#49-improvement-local-delivery-dedupe-and-debounce) |
| **WebSockets, then inbound publish** through stream `to` targets | 7 | [§4.8](streaming.md#48-future-protocols-websockets-and-webrtc) |
| **WebRTC data channels,** in the separate `Runax.Hookpipe.Streaming.WebRtc` package | 8 | [§4.8](streaming.md#48-future-protocols-websockets-and-webrtc) |

## Small PRs, any time

| PR | What |
| --- | --- |
| One type per file | Move the nested `CompareCondition.SelectedKind`, `CompareCondition.SelectedValue`, and `HookpipeMetrics.RequestTimer` into their own files. |
| Pin the Kafka image | `compose.yml` and `ci.yml` use `apache/kafka:latest`, and Docker Hub failed on it once. Pin a version, including the `ancestor=` filter in `ci.yml`. |
| Refresh `TODO.md` | It still says "sinks", and some of its items are done or covered here. Point it at this file for the v2 work. |
