# Configuration

Hookpipe is configured via a YAML file and environment variables.

## Config file

Default path: `config/hookpipe.yaml`. Override with `HOOKPIPE_CONFIG_PATH` env var.

### Endpoints

Each endpoint defines an HTTP route that Hookpipe listens on.

```yaml
endpoints:
    - id: github-push # Unique identifier
      path: /github/push # URL path (supports {params})
      methods: # Accepted HTTP methods (default: POST)
          - POST
      validation: # Optional — see Validation section
          signature:
              header: X-Hub-Signature-256
              secret_env: GITHUB_WEBHOOK_SECRET
              algorithm: hmac-sha256
      sink: my-sink # Single sink (backwards compatible)
      message: # Controls what goes into the envelope
          include_body: true # Include request body (default: true)
          include_headers: true # Include request headers (default: false)
          header_filter: # Only include these headers (optional)
              - X-GitHub-Event
              - X-GitHub-Delivery
          metadata: # Static or path-param metadata (optional)
              source: "{source}" # {param} is resolved from path
```

#### Fan-out (multiple sinks)

Use `sinks` (plural) to route a single endpoint to multiple destinations:

```yaml
endpoints:
    - id: github-push
      path: /github/push
      sinks: # Fan-out to multiple sinks
          - rabbitmq-main
          - stdout-dev
          - relay-downstream
      message:
          include_body: true
```

Both `sink` (single string) and `sinks` (list) are supported. If both are set, `sinks` takes precedence.

### Sinks

Each sink defines where messages are sent.

```yaml
sinks:
    - id: my-sink
      type: stdout # Sink type (see below)
      settings: # Type-specific settings
          key: value
```

#### Stdout

Logs the message envelope to console as JSON. For development only.

```yaml
sinks:
    - id: dev
      type: stdout
```

#### RabbitMQ

Publishes messages to a RabbitMQ exchange.

```yaml
sinks:
    - id: rabbitmq-main
      type: rabbitmq
      settings:
          url_env: RABBITMQ_URL # Env var holding the AMQP connection string
          exchange: webhooks # Exchange name (default: "")
          routing_key: events # Routing key (default: "")
```

The exchange is declared as `topic` and `durable` on startup.

#### Kafka

Produces messages to a Kafka topic.

```yaml
sinks:
    - id: kafka-events
      type: kafka
      settings:
          brokers_env: KAFKA_BROKERS # Env var holding the broker list
          topic: webhook-events # Kafka topic (required)
```

Uses idempotent producer with `Acks.All`. Message key is the endpoint ID.

#### SQS

Sends messages to an AWS SQS queue.

```yaml
sinks:
    - id: sqs-main
      type: sqs
      settings:
          queue_url_env: SQS_QUEUE_URL # Env var holding the queue URL
          region_env: AWS_REGION # Env var holding the region (optional)
```

AWS credentials are resolved via the default credential chain.

#### HTTP Relay

Forwards messages to another HTTP endpoint.

```yaml
sinks:
    - id: relay-downstream
      type: http
      settings:
          url_env: HTTP_RELAY_URL # Env var holding the target URL
          timeout_seconds: "30" # Request timeout (default: 30)
```

## Validation

Optional per-endpoint request validation. Only one method per endpoint.

### Bearer token

Compares the `Authorization` header against a secret from an env var.

```yaml
validation:
    auth:
        type: bearer
        token_env: MY_WEBHOOK_TOKEN # Env var holding the expected token
```

Accepts both `Bearer <token>` and raw `<token>` formats.

### HMAC-SHA256

Computes HMAC-SHA256 of the request body and compares to the signature header.

```yaml
validation:
    signature:
        header: X-Hub-Signature-256 # Header containing the signature
        secret_env: GITHUB_SECRET # Env var holding the signing secret
        algorithm: hmac-sha256 # Algorithm identifier
```

Handles signatures with or without prefix (e.g. `sha256=<hex>`).

### Stripe signature

Validates using Stripe's v1 signing scheme with timestamp freshness check.

```yaml
validation:
    signature:
        header: Stripe-Signature
        secret_env: STRIPE_WEBHOOK_SECRET
        algorithm: stripe-v1
```

### API key

Compares a custom header value against an env var.

```yaml
validation:
    auth:
        type: api-key
        header: X-API-Key
        token_env: MY_API_KEY
```

### IP allowlist

Restricts requests to specific IPs or CIDRs.

```yaml
validation:
    auth:
        type: ip-allowlist
        token_env: ALLOWED_IPS # e.g. "192.168.1.0/24,10.0.0.1"
```

## Retry policy

Optional per-sink retry with exponential backoff and jitter. If a sink produce fails, Hookpipe retries before returning an error.

```yaml
sinks:
    - id: rabbitmq-main
      type: rabbitmq
      settings:
          url_env: RABBITMQ_URL
          exchange: webhooks
      retry:
          max_retries: 3
          delay_seconds: 2
          backoff_multiplier: 2
```

| Key                  | Description                                | Default |
| -------------------- | ------------------------------------------ | ------- |
| `max_retries`        | Maximum retry attempts                     | `3`     |
| `delay_seconds`      | Initial delay before first retry           | `2`     |
| `backoff_multiplier` | Exponential multiplier (e.g. 2s, 4s, 8s)  | `2`     |

If no `retry` block is defined, the sink produces once with no retries (current default behavior). Each retry attempt is logged as a warning with the attempt number and exception message.

## Environment variables

| Variable                                  | Description                                         | Default                |
| ----------------------------------------- | --------------------------------------------------- | ---------------------- |
| `HOOKPIPE_CONFIG_PATH`                    | Path to YAML config file                            | `config/hookpipe.yaml` |
| `HOOKPIPE_MAX_BODY_SIZE_MB`               | Max request body size in MB. Returns 413 if exceeded | `~28.6` (Kestrel default) |
| `Logging__LogLevel__Default`              | Default log level                                   | `Information`          |
| `Logging__LogLevel__Hookpipe`             | Hookpipe log level                                  | `Information`          |
| `Logging__LogLevel__Microsoft.AspNetCore` | ASP.NET log level                                   | `Warning`              |
| `RABBITMQ_URL`                            | RabbitMQ connection string (if using RabbitMQ sink) | —                      |
| `KAFKA_BROKERS`                           | Kafka broker list (if using Kafka sink)             | —                      |
| `HTTP_RELAY_URL`                          | HTTP relay target URL (if using HTTP sink)          | —                      |
| `SQS_QUEUE_URL`                           | SQS queue URL (if using SQS sink)                   | —                      |
| `AWS_REGION`                              | AWS region for SQS (optional)                       | SDK default            |
| `SEQ_URL`                                 | Seq server URL (if using Seq logging)               | — (disabled)           |
| `SEQ_API_KEY`                             | Seq API key                                         | — (optional)           |
| `LOKI_URL`                                | Grafana Loki URL (if using Loki logging)            | — (disabled)           |

Secrets referenced by `secret_env`, `token_env`, and `url_env` in config are resolved from environment variables at runtime.

## Hot-reload

Hookpipe watches the config file for changes and reloads automatically. No restart needed.

**What can be changed at runtime:**

- Validation rules (add, remove, or change auth/signature config)
- Sink routing (point an endpoint to a different sink)
- Message config (include body, headers, metadata, header filters)
- Allowed HTTP methods per endpoint

**What requires a restart:**

- Adding new endpoint paths (routes are registered once at startup)
- Adding new sinks (connections are created at startup)
- Removing endpoint paths

Changes are debounced (500ms) to avoid reloading multiple times during a save. If the new config is invalid, the previous config is kept and an error is logged.
