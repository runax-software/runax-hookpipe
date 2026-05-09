# Roadmap

## v0.2.0

### Sinks
- [ ] Redis Streams sink
- [ ] Azure Service Bus sink
- [ ] Google Pub/Sub sink
- [ ] NATS sink

### Validators
- [ ] Stripe signature validator (`stripe-v1`)
- [ ] IP allowlist validator
- [ ] API key header validator (custom header + key)

### Features
- [ ] Retry policy on sink failures (exponential backoff, dead-letter config)
- [ ] Request/response transformation (modify body before producing)
- [ ] Rate limiting per endpoint
- [ ] Webhook replay (persist failed messages, retry later)

## v0.3.0

### Architecture
- [ ] Extract handler into `WebhookHandler` class
- [ ] Plugin system (load sinks/validators from external assemblies)
- [ ] Multi-tenant support (API key -> tenant -> isolated config)

### Observability
- [ ] OpenTelemetry tracing
- [ ] Grafana dashboard template
- [ ] Healthcheck per sink (verify connectivity)

### Deployment
- [ ] Helm chart for Kubernetes
- [ ] Docker Compose full stack (Hookpipe + RabbitMQ + Prometheus + Grafana)
- [ ] ARM/multi-arch Docker images

## v0.4.0

### Features
- [ ] Conditional routing (route to different sinks based on body/header content)
- [ ] Request deduplication (idempotency key from header or body field)
- [ ] Delayed/scheduled delivery (produce after N seconds)
- [ ] Config schema validation (JSON Schema for YAML)

### Sinks
- [ ] Amazon SNS sink
- [ ] Amazon EventBridge sink
- [ ] Webhook batching (buffer N messages, flush as array)

## v0.5.0

### Security
- [ ] mTLS support for sink connections
- [ ] Encrypted config values (secrets encrypted at rest, decrypted at startup)

### Performance
- [ ] Parallel sink fan-out (produce to multiple sinks concurrently)
- [ ] Connection pooling for HTTP relay sink
- [ ] Request buffering (accept 202 immediately, produce async in background)

## v1.0.0

### Stability
- [ ] All public APIs finalized — no breaking changes to config format
- [ ] All sinks tested in production
- [ ] Full integration test suite for every sink
- [ ] Load testing and benchmarks published
- [ ] Migration guide from v0.x

### Documentation
- [ ] OpenAPI spec generation for registered endpoints
- [ ] Web UI for config management and live request monitoring
- [ ] Complete Grafana dashboard with all metrics

### Deployment
- [ ] Stable Helm chart published
- [ ] Docker Hub / GHCR multi-arch images (amd64, arm64)
