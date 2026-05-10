# Changelog

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
