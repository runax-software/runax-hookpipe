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
      sink: my-sink # Sink ID to route messages to
      message: # Controls what goes into the envelope
          include_body: true # Include request body (default: true)
          include_headers: true # Include request headers (default: false)
          header_filter: # Only include these headers (optional)
              - X-GitHub-Event
              - X-GitHub-Delivery
          metadata: # Static or path-param metadata (optional)
              source: "{source}" # {param} is resolved from path
```

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
| `SEQ_URL`                                 | Seq server URL (if using Seq logging)               | — (disabled)           |
| `SEQ_API_KEY`                             | Seq API key                                         | — (optional)           |
| `LOKI_URL`                                | Grafana Loki URL (if using Loki logging)            | — (disabled)           |

Secrets referenced by `secret_env`, `token_env`, and `url_env` in config are resolved from environment variables at runtime.
