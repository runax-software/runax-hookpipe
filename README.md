# Hookpipe

Config-driven webhook gateway. Receive HTTP webhooks, validate them, and route messages to queues.

## How it works

```
External service (GitHub, Stripe, Coolify, etc.)
  → POST https://hooks.yourdomain.com/github/push
    → Hookpipe matches route from config
      → validates request (signature, bearer token)
        → wraps in message envelope
          → produces to sink (RabbitMQ, stdout)
            → returns 202 Accepted
```

## Quick start

```bash
# Clone and build
git clone https://github.com/runax-software/runax-hookpipe.git
cd runax-hookpipe
dotnet restore && dotnet build

# Copy and edit env
cp .env.example .env

# Start RabbitMQ (optional)
docker compose up -d

# Run
dotnet run --project src/Hookpipe.API

# Test
curl -X POST http://localhost:5000/test/webhook \
  -H "Content-Type: application/json" \
  -d '{"hello":"world"}'
```

## Config

Define endpoints and sinks in `config/hookpipe.yaml`:

```yaml
endpoints:
    - id: github-push
      path: /github/push
      methods:
          - POST
      validation:
          signature:
              header: X-Hub-Signature-256
              secret_env: GITHUB_WEBHOOK_SECRET
              algorithm: hmac-sha256
      sink: rabbitmq-main
      message:
          include_body: true
          include_headers: true
          header_filter:
              - X-GitHub-Event
              - X-GitHub-Delivery

sinks:
    - id: rabbitmq-main
      type: rabbitmq
      settings:
          url_env: RABBITMQ_URL
          exchange: webhooks
          routing_key: events
```

See [Configuration](docs/configuration.md) for full reference.

## Hot-reload

Hookpipe watches the config file and reloads automatically when it changes. Validation rules, sink routing, message config, and allowed methods can be changed without restart. See [Configuration](docs/configuration.md#hot-reload) for details.

## Sinks

| Sink     | Type       | Status    |
| -------- | ---------- | --------- |
| Stdout   | `stdout`   | Available |
| RabbitMQ | `rabbitmq` | Available |
| Kafka    | `kafka`    | Available |
| SQS      | `sqs`      | Planned   |

See [Sinks](docs/sinks.md) for details.

## Validators

| Validator    | Type          | Use case                 |
| ------------ | ------------- | ------------------------ |
| Bearer token | `bearer`      | Coolify, custom webhooks |
| HMAC-SHA256  | `hmac-sha256` | GitHub, Shopify          |

See [Validators](docs/validators.md) for details.

## Message envelope

Every webhook is wrapped in a standardized envelope:

```json
{
    "id": "13979c7e-108c-48b1-aac5-ff9220a3875b",
    "endpointId": "github-push",
    "receivedAt": "2026-05-07T16:23:55.644839+00:00",
    "method": "POST",
    "path": "/github/push",
    "remoteAddress": "192.30.252.1",
    "headers": { "X-GitHub-Event": "push" },
    "body": { "ref": "refs/heads/main" },
    "metadata": {}
}
```

See [Message Envelope](docs/message-envelope.md) for full format.

## Environment variables

| Variable                     | Description                | Default                |
| ---------------------------- | -------------------------- | ---------------------- |
| `HOOKPIPE_CONFIG_PATH`       | Path to YAML config        | `config/hookpipe.yaml` |
| `HOOKPIPE_MAX_BODY_SIZE_MB`  | Max request body size (MB) | `~28.6` (Kestrel default) |
| `RABBITMQ_URL`               | RabbitMQ connection string | —                      |
| `KAFKA_BROKERS`              | Kafka broker list          | —                      |
| `SEQ_URL`                    | Seq server URL             | — (disabled)           |
| `LOKI_URL`                   | Grafana Loki URL           | — (disabled)           |

See [Configuration](docs/configuration.md) for all env vars and [Logging](docs/logging.md) for log destinations.

## Endpoints

| Method | Path      | Description                 |
| ------ | --------- | --------------------------- |
| `GET`  | `/health` | Liveness check              |
| `*`    | `/*`      | Dynamic — defined in config |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
