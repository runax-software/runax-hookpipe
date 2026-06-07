# Roadmap

## v0.2.0 (done)

### Sinks

- [x] Redis Streams sink
- [x] Google Pub/Sub sink
- [x] Amazon SNS sink
- [x] Amazon EventBridge sink

### Validators

- [x] Stripe signature validator (`stripe-v1`)
- [x] IP allowlist validator
- [x] API key header validator (custom header + key)

### Features

- [x] Retry policy on sink failures (exponential backoff)
- [x] Rate limiting per endpoint

## v0.3.0

### Architecture

- [x] Extract handler into `WebhookHandler` class

### Features

- [x] Conditional routing (route to different sinks based on body/header content)
- [ ] Healthcheck per sink (verify connectivity)
- [x] Parallel sink fan-out (produce to multiple sinks concurrently)

### Sinks

- [x] Azure Service Bus sink
- [x] Azure Event Hub sink

### Deployment

- [ ] Docker Compose full stack (Hookpipe + RabbitMQ + Prometheus + Grafana)

## v0.4.0

### Features

- [ ] Request deduplication (idempotency key from header or body field)
- [ ] Request/response transformation (modify body before producing)
- [ ] Webhook batching (buffer N messages, flush as array)
- [ ] Request buffering (accept 202 immediately, produce async in background)
- [ ] OpenTelemetry tracing

### Observability

- [ ] Grafana dashboard template

### Sinks

- [ ] Azure Event Grid sink
- [ ] NATS sink

## v0.5.0

### Resilience

- [ ] Webhook replay / dead-letter store (persist failed messages, replay later)
- [ ] Delayed/scheduled delivery (produce after N seconds)

### Deployment

- [ ] Helm chart for Kubernetes
- [ ] ARM/multi-arch Docker images

## v1.0.0

### Stability

- [ ] All public APIs finalized — no breaking changes to config format
- [ ] All sinks tested in production
- [ ] Full integration test suite for every sink
- [ ] Load testing and benchmarks published
- [ ] Migration guide from v0.x
- [ ] Complete Grafana dashboard with all metrics
