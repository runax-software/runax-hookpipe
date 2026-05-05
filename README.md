# Hookpipe

Config-driven HTTP-to-message-queue gateway. Define endpoints in config, receive webhooks, produce messages to SQS, Kafka, RabbitMQ, or other sinks.

## Why

- Services like GitHub, Stripe, Coolify, etc. send webhooks over HTTP
- You often need those events in a message queue for async processing
- Writing a small HTTP handler per integration is repetitive
- Hookpipe is the glue — configure once, route webhooks to queues

## How it works

```
External service (GitHub, Stripe, etc.)
  → POST https://webhooks.yourdomain.com/github/push
    → Hookpipe matches route from config
      → validates request (optional: signature, headers, schema)
        → produces message to configured sink (SQS, Kafka, etc.)
          → returns 200 to caller
```

## Config

```yaml
endpoints:
  - id: github-push
    path: /github/push
    method: POST
    # optional request validation
    validation:
      signature:
        header: X-Hub-Signature-256
        secret_env: GITHUB_WEBHOOK_SECRET
        algorithm: hmac-sha256
    sink: sqs-main
    # what goes into the message
    message:
      # include full request body, headers, or transform
      include_headers: [X-GitHub-Event, X-GitHub-Delivery]
      include_body: true

  - id: stripe-events
    path: /stripe/events
    method: POST
    validation:
      signature:
        header: Stripe-Signature
        secret_env: STRIPE_WEBHOOK_SECRET
        algorithm: stripe-v1
    sink: kafka-events
    message:
      include_body: true

  - id: coolify-deploy
    path: /coolify/deploy
    method: POST
    validation:
      auth:
        type: bearer
        token_env: COOLIFY_WEBHOOK_TOKEN
    sink: sqs-main
    message:
      include_headers: true
      include_body: true

  - id: custom-ingest
    path: /ingest/{source}
    method: [POST, PUT]
    sink: kafka-events
    message:
      include_body: true
      metadata:
        source: "{source}" # from path param

sinks:
  - id: sqs-main
    type: sqs
    queue_url_env: SQS_MAIN_QUEUE_URL
    region_env: AWS_REGION

  - id: kafka-events
    type: kafka
    brokers_env: KAFKA_BROKERS
    topic: webhook-events

  - id: rabbitmq-main
    type: rabbitmq
    url_env: RABBITMQ_URL
    exchange: webhooks
    routing_key: events
```

## Message envelope

Every message produced follows a consistent envelope:

```json
{
  "id": "msg-uuid",
  "endpoint_id": "github-push",
  "received_at": "2026-05-05T12:00:00Z",
  "method": "POST",
  "path": "/github/push",
  "remote_addr": "192.30.252.1",
  "headers": {
    "X-GitHub-Event": "push",
    "X-GitHub-Delivery": "delivery-uuid"
  },
  "body": {},
  "metadata": {}
}
```

## Core components

### Router
- Builds routes from config at startup
- Matches incoming requests to endpoint definitions
- Supports path params (`/ingest/{source}`)

### Validator
- Runs before producing — rejects invalid requests with 401/403
- Pluggable per endpoint:
  - **HMAC signature** — GitHub, generic webhooks
  - **Stripe signature** — Stripe's `Stripe-Signature` header scheme
  - **Bearer token** — simple token comparison
  - **None** — no validation (open endpoint)

### Producer
- Sends the message envelope to the configured sink
- Each sink type is a plugin/implementation behind a common interface
- Handles retries and connection pooling internally

### Sink types

| Sink | Config |
|------|--------|
| SQS | queue URL, region |
| Kafka | brokers, topic |
| RabbitMQ | URL, exchange, routing key |
| HTTP | forward to another URL (webhook relay) |
| Stdout | log to stdout (development/debugging) |

## Request lifecycle

```
1. Receive HTTP request
2. Match route from config → 404 if no match
3. Validate method → 405 if wrong method
4. Run validation (signature/auth) → 401/403 if invalid
5. Build message envelope
6. Produce to sink → 500 if sink fails
7. Return 200 (or 202 if async)
```

## Config reload

- Watch config file for changes, reload routes without restart
- Or expose `POST /admin/reload` (admin-only) to trigger reload

## Observability

- Structured logging per request (endpoint ID, sink, success/failure)
- Metrics: requests received, messages produced, validation failures, sink errors
- Health endpoint: `GET /health`

## Deployment

Container on Coolify. Config file mounted as volume or loaded from env.
