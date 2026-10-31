# Changelog

## Unreleased

v2 is a breaking release with no compatibility mode. See [docs/upgrading.md](docs/upgrading.md).

### Breaking
- **Config format v2.** Files must start with `version: 2`; v1 files are refused at startup with a link to the upgrade guide.
  - `endpoints` become `webhooks`, a map keyed by ID, served under `server.paths.webhooks` (`/webhooks` by default; `/` keeps v1 URLs).
  - `sinks` become `brokers` (the connection); webhooks publish to inline targets such as `rabbit:github.push`.
  - `sink`, `sinks`, and `routing` become `to` or `route` with `when` conditions. A route ends with a rule that has no `when`; there's no implicit fallback.
  - `validation` becomes `validate`, a list where every validator must pass.
  - `*_env` keys are replaced by `${VAR}`, `${VAR:-default}`, and `${file:…}` interpolation. A missing variable fails startup.
  - Durations are written `2s`, `1m`; `backoff_multiplier` becomes `factor` and is now applied.
  - Unknown keys fail the load, with the config path of every problem.
- The `HOOKPIPE_MAX_BODY_SIZE_MB` environment variable is replaced by `server.max_body_size`, written as a size such as `25MB`.
- Broker types renamed: `redis-stream` → `redis`, `servicebus` → `azure-servicebus`, `eventhub` → `azure-eventhubs`.
- `/health/sinks` → `/health/brokers`.
- 202 body is `{"status":"accepted","webhook_id":…,"message_id":…}`; 500 body error is `publish_failed`.
- Envelope `endpointId` → `webhookId`; header/attribute `hookpipe.endpoint.id` → `hookpipe.webhook.id`.
- Metrics: `hookpipe_messages_produced_total` → `hookpipe_messages_published_total`, `hookpipe_sink_errors_total` → `hookpipe_publish_errors_total`, labels `endpoint_id` → `webhook_id` and `sink_id` → `broker_id`.

### Added
- Routing on the JSON body with RFC 9535 JSONPath, plus header and path-parameter conditions; operators `eq`, `ne`, `in`, `not_in`, `gt`, `gte`, `lt`, `lte`, `contains`, `starts_with`, `ends_with`, `regex`, `exists`, combined with `all`, `any`, and `not`.
- Templates in target topics and `message.metadata` values: path parameters, `{webhook_id}`, `{metadata.key}`, `{header.Name}`, and `{$.json.path}` with `:-` defaults. Topics render per target; an unresolved or invalid one skips only that target and is counted in `hookpipe_template_failures_total`.
- Path-parameter placeholders in condition values, such as `{ header: X-Tenant, eq: "{tenant}" }`.
- `server.max_body_size` (`25MB`, `64KB`, `512B`, `1GB`) sets the service's Kestrel body limit for the whole process; library mode ignores it with a warning, because the limit belongs to the host's server.
- `webhooks.<id>.max_body_size` limits one webhook's body and answers 413 above it. It's applied per request, so it works in library mode too and changes live on reload.
- Retry `backoff` (`constant`, `linear`, `exponential`), `max_delay`, and `jitter`.
- Reloads that need a restart are logged; a reload whose targets use a broker that isn't running is rejected.

## v0.2.1

### Fixed
- Record `hookpipe_messages_produced_total` after successful sink publishes.
- Record `hookpipe_sink_errors_total` when configured sink publishes fail.

## v0.2.0

### New sinks
- **Redis Streams** (`redis-stream`) — append messages to Redis streams
- **Google Pub/Sub** (`google-pubsub`) — publish to GCP Pub/Sub topics with emulator support
- **SNS** (`sns`) — publish to AWS SNS topics via LocalStack-compatible `service_url_env`
- **EventBridge** (`eventbridge`) — put events to AWS EventBridge buses with configurable source and detail type

### New validators
- **Stripe signature** (`stripe-v1`) — Stripe's v1 signing scheme with timestamp freshness check
- **API key** (`api-key`) — custom header + key validation
- **IP allowlist** (`ip-allowlist`) — restrict by IP address or CIDR range

### Features
- **Retry policy** — per-sink exponential backoff with jitter via Polly
- **Rate limiting** — per-endpoint fixed window rate limiter, returns 429
- **Fan-out** — route one endpoint to multiple sinks (`sinks:` list)
- **Config hot-reload** — file watcher detects changes, reloads without restart

### Observability
- **Prometheus metrics** at `/metrics` — requests, messages, errors, latency, validation failures
- **Structured logging** with `[Hookpipe.Module:Type:Id]` convention
- **Serilog** with Seq and Grafana Loki support
- **Request correlation ID** via `UseSerilogRequestLogging`

### Infrastructure
- Migrated Docker publishing from Docker Hub to **GHCR**
- Added LocalStack for AWS sink integration tests (SQS, SNS, EventBridge)
- Added Google Pub/Sub emulator for integration tests
- Added Redis to Docker Compose for local development
- **89 unit tests + 15 integration tests**

### Improvements
- `SinkHelper` — shared utilities for env var loading and JSON serialization
- `SinkFactory` and `ValidatorFactory` — centralized instantiation with `TypeName` constants
- `ConfigLoader` reads known validator types from `ValidatorFactory.KnownTypes`
- Constant-time comparison on all token/signature validators
- `LogHelper.MaskUri` — credentials masked in all connection URL logs
- Configurable request body size limit via `HOOKPIPE_MAX_BODY_SIZE_MB`

## v0.1.0

Initial release.

### Sinks
- **Stdout** — console output for development and debugging
- **RabbitMQ** — publish to exchanges with persistent delivery
- **Kafka** — idempotent producer with `Acks.All`
- **HTTP Relay** — forward webhooks to another URL
- **SQS** — send to AWS SQS queues with message attributes
- **Fan-out** — route one endpoint to multiple sinks simultaneously

### Validators
- **Bearer token** — constant-time comparison against env var secret
- **HMAC-SHA256** — signature verification compatible with GitHub, Shopify, and others

### Config
- YAML-based configuration for endpoints and sinks
- Hot-reload — file watcher detects changes and reloads without restart
- All secrets resolved from environment variables at runtime
- Configurable request body size limit (`HOOKPIPE_MAX_BODY_SIZE_MB`)

### Observability
- **Serilog** structured logging with `[Hookpipe.Module:Type:Id]` convention
- **Seq** support (enabled via `SEQ_URL` env var)
- **Grafana Loki** support (enabled via `LOKI_URL` env var)
- **Prometheus** metrics at `/metrics` endpoint
  - `hookpipe_requests_total` — requests by endpoint, method, status
  - `hookpipe_messages_produced_total` — messages produced per sink
  - `hookpipe_sink_errors_total` — sink failures
  - `hookpipe_validation_failures_total` — auth/signature rejections
  - `hookpipe_request_duration_seconds` — processing latency histogram
- Request correlation ID via Serilog

### Infrastructure
- Dockerfile (Alpine-based, multi-stage build)
- Docker Compose for local development (RabbitMQ + Kafka)
- GitHub Actions CI (build, test, format check, integration tests)
- Docker Hub publish workflow on release
- 48 unit tests + 5 integration tests

### Documentation
- Configuration reference
- Sinks reference
- Validators reference
- Message envelope format
- Logging guide
- Metrics guide
- CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md
- AGENTS.md for AI coding agents
