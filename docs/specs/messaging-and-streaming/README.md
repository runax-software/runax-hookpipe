# Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service

| | |
| --- | --- |
| **Status** | Draft |
| **Date** | 2026-10-27 |
| **Replaces** | The hand-written `ISink` implementations, `SinkFactory`, config format v1, the `Hookpipe.Core` / `Hookpipe.API` project names, and the `hookpipe` image name |
| **Depends on** | [runax-messaging](https://github.com/runax-software/runax-messaging) 2.x, plus the changes in [§7](messaging-prerequisites.md#7-prerequisite-changes-in-runax-messaging) |
| **Roadmap** | Covers "Request buffering" and part of "OpenTelemetry tracing" (v0.4.0 in [TODO.md](../../../TODO.md)), and adds streaming |

## Summary

- **One messaging library.** Hookpipe stops maintaining its own broker clients. Each broker in
  the config becomes a runax-messaging **bus** (one bus, one transport). The 11 `ISink` classes
  and their SDK packages go away. `stdout` and `http`, which runax-messaging has no transport
  for, become small publish-only transports inside Hookpipe, so every publish goes through
  `IBus`. EventBridge moves into runax-messaging as a new transport (P8).
- **Publish workers, one per broker by default.** Every broker Hookpipe publishes to gets a
  bounded queue and, by default, exactly one worker that drains it. Only that worker publishes
  to the broker, in queue order.
  - HTTP handlers only enqueue. Depending on the webhook's `ack` mode, they wait for the result
    or return right away.
  - Back-pressure becomes visible (a full queue returns 503) instead of piling up request
    threads.
  - The worker batches when it can, so ordering costs little throughput.
  - `publish.workers: N` adds parallelism. Messages with the same `order_by` key (by default,
    the topic) always go to the same worker, so ordering holds per key.
  - Workers are async loops, not OS threads.
- **Streaming, broker → client.** A `streams` section reads broker topics and pushes messages to
  connected clients. Server-Sent Events ship first.
  - The client-facing protocol is a plug-in (`IStreamProtocol`), so WebSockets and WebRTC data
    channels can be added later without touching subscription, filtering, replay, or
    back-pressure.
  - Each stream has exactly one worker. It owns the event sequence, the replay buffer, and the
    subscriber list, so none of them needs a lock.
  - A planned improvement delivers this replica's own publishes from memory and drops the
    broker's later copy by message ID. Optional debounce sends only the latest event per key.
- **One config file, reworked.** `config/hookpipe.yaml` holds three things: `brokers`,
  `webhooks`, and `streams`.
  - There's no sink section. Webhooks publish *to* targets written inline
    (`to: rabbit:github.push`), and streams read *from* them. With WebSockets, streams will also
    send *to* them.
  - Webhooks live under `/webhooks/<path>` and streams under `/streams/<path>`. Both prefixes are
    configurable.
  - Routes and stream filters can match the JSON body with JSONPath
    (`{ jsonpath: "$.status", eq: paid }`), combined with `all`, `any`, and `not`. Topics and
    ordering keys can come from body values (`{$.customer.id}`).
  - Format v2 allows several validators per webhook, uses `${ENV}` interpolation, and rejects
    unknown keys.
  - v1 files aren't supported. v2 is a breaking release: Hookpipe refuses a v1 file at
    startup and points to the upgrade guide.
- **A library and a service.** The project becomes **Runax.Hookpipe**: a NuGet package that
  holds the whole engine, plus `Runax.Hookpipe.Api`, a thin self-hosted service built only on
  the package's public API. You can add Hookpipe to your own ASP.NET Core app, or run it as a
  microservice from the `ghcr.io/runax-software/runax-hookpipe` image. Both use the same config
  format, and library users can also put it in `appsettings.json` or define it in code. The old
  `hookpipe` package is deleted once its two releases are copied to the new name, so there's
  only one package.
- **Library work first.** Three runax-messaging changes block this: consumers registered from a
  delegate with a runtime topic (P1), a partition key (P2), and adding consumers to any bus
  outside its setup block (P6). The rest have workarounds (P3–P5, P7, P8).

## Documents

| File | Covers |
| --- | --- |
| [architecture.md](architecture.md) | §3: terms, publish workers per broker and going faster, ingress, egress, broadcast per replica |
| [streaming.md](streaming.md) | §4: connection lifecycle, the protocol seam, stream workers, filters, the SSE adapter, WebSockets and WebRTC later, local delivery with dedupe and debounce |
| [configuration.md](configuration.md) | §5: config format v2: `server`, `brokers`, targets, `webhooks`, conditions with JSONPath and templates, `streams`, upgrading from v1, hot reload |
| [messaging-integration.md](messaging-integration.md) | §6: wiring buses, Hookpipe-local transports, wire changes, health, telemetry |
| [messaging-prerequisites.md](messaging-prerequisites.md) | §7: the runax-messaging changes P1–P8 |
| [implementation-plan.md](implementation-plan.md) | §8–§10: code layout, failure modes, phases and tests |
| [packaging.md](packaging.md) | §11: the rename to Runax.Hookpipe, the NuGet library and the self-hosted service, the Docker image, releases |
| [open-questions.md](open-questions.md) | §12: open questions |
| [breaking-changes.md](breaking-changes.md) | §13: every breaking change from v1, in one list for the release notes |

## 1. Problem

### What's wrong today

1. **Duplicate broker code.** `Hookpipe.Core/Sinks` holds hand-written clients for RabbitMQ,
   Kafka, SQS, SNS, Redis Streams, Google Pub/Sub, Azure Service Bus, and Azure Event Hubs.
   runax-messaging already ships transports for all eight, with health checks, native headers,
   tracing, metrics, and AOT support. Every broker fix lands twice, or only once.
2. **Publishing runs on the request.** `WebhookHandler.ProduceToSinks` calls
   `sink.ProduceAsync` for every sink in parallel with `Task.WhenAll`, inside the HTTP request:
   - Retries with exponential backoff (2 s, 4 s, 8 s by default) hold the request open, and so
     does a slow broker.
   - Concurrent requests call into one client at the same time. `RabbitMqSink` shares one
     `IChannel` across all requests and relies on the client library to serialize publishes.
   - Nothing bounds the work. A broker outage shows up as request latency and thread-pool
     growth, never as a clear "this sink is full" signal.
   - Retries reorder messages. While message A waits 2 s to retry, B, C, and D get published.
   - A caller that disconnects cancels the publish (`context.RequestAborted` is passed through),
     so a webhook that was received and validated can be dropped.
3. **Config rough edges.**
   - `sink`, `sinks`, and `routing` are three ways to say where a message goes.
   - Only one validation method is allowed per endpoint, so you can't require both an HMAC
     signature and an IP allowlist.
   - `ip-allowlist` puts a CIDR list in `token_env`.
   - `retry.backoff_multiplier` is documented but ignored. `SinkFactory.CreateRetryPipelines`
     never reads it.
   - Secrets are read from the environment on every request, so a missing secret is a 401 at
     runtime instead of an error at startup.
   - `IgnoreUnmatchedProperties()` hides typos. `inlcude_body: false` is silently ignored.
   - `settings` is an untyped `Dictionary<string, string>`.
4. **One direction only.** Hookpipe can move HTTP → broker but not broker → browser. Live
   dashboards, deploy feeds, and webhook inspectors each need their own small service today.
5. **Service only.** A team that already runs an ASP.NET Core app can't add Hookpipe's
   endpoints or streams to it. They have to deploy and operate one more service.

### Goals

- Every publish and subscribe goes through runax-messaging.
- Each broker is published to by its own workers, one by default: serialized access, ordering
  by queue or by key, bounded memory.
- Broker → client streaming over SSE, with resume (`Last-Event-ID`), heartbeats, server-side
  filters, and per-client back-pressure.
- A protocol seam that WebSockets and WebRTC can plug into without changes to the stream core.
- One config file for both directions, reworked and validated strictly at load.
- One codebase used two ways: as the `Runax.Hookpipe` NuGet library inside any ASP.NET Core
  app, and as the self-hosted service.
- A clean break. Nothing in v2 exists only for compatibility: no v1 config translator, no
  aliases, no old names. Every breaking change is listed in [§13](breaking-changes.md).

### Non-goals

- Durable buffering. Publish queues live in memory, and a crash loses what's queued. A persisted
  replay or dead-letter store stays on the v0.5.0 roadmap.
- Exactly-once delivery, in either direction.
- WebSocket and WebRTC adapters. This spec designs for them; it doesn't build them.
- Clients publishing *into* the broker over a stream connection. The seam is reserved
  ([§4.8](streaming.md#48-future-protocols-websockets-and-webrtc)).
- Changing the message envelope beyond renaming `endpointId` to `webhookId`.
- Adding brokers, webhooks, or streams at runtime without a restart. That's the same limit as v1.
- Native AOT for the library.

## 2. Today's behavior (baseline)

```
POST /github/push
  → WebhookHandler.HandleAsync                 (one per endpoint, built in Program.cs)
    → method check → rate limit → validate      (env secrets read per request)
    → resolve sinks (routing rules / sinks / sink)
    → EnvelopeBuilder.BuildAsync
    → Task.WhenAll(sinks.Select(ProduceAsync))  (Polly retry per sink, on the request)
  → 202 if ≥1 sink succeeded, else 500
```

Sinks are created eagerly in `Program.cs` from `SinkFactory.CreateAllAsync` and disposed on
`ApplicationStopping`. Routes, sinks, and rate limiters are fixed at startup. Validation,
routing, message settings, and methods hot-reload through `ConfigProvider.Current`.
