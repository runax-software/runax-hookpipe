# Hookpipe

[![GHCR](https://img.shields.io/badge/GHCR-hookpipe-blue?logo=github)](https://github.com/runax-software/runax-hookpipe/pkgs/container/hookpipe)
[![CI](https://github.com/runax-software/runax-hookpipe/actions/workflows/ci.yml/badge.svg)](https://github.com/runax-software/runax-hookpipe/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Config-driven webhook gateway. Receive HTTP webhooks, validate them, and route messages to queues.

## How it works

```
External service (GitHub, Stripe, Coolify, etc.)
  → POST https://hooks.yourdomain.com/webhooks/github
    → Hookpipe matches the webhook from config
      → validates the request (signature, bearer token, IP)
        → picks targets, optionally by header or JSON body (JSONPath)
          → wraps the request in a message envelope
            → publishes to brokers (RabbitMQ, Kafka, SQS, HTTP, stdout, …)
              → returns 202 Accepted
```

## Quick start

### Docker

```bash
docker run -p 8080:8080 \
  -v ./config:/app/config \
  -e RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672 \
  ghcr.io/runax-software/runax-hookpipe:latest
```

Releases up to 0.2.1 were published as `ghcr.io/runax-software/hookpipe`. The next release is the
first under the new name.

### From source

```bash
git clone https://github.com/runax-software/runax-hookpipe.git
cd runax-hookpipe
cp .env.example .env

# Start RabbitMQ + Kafka (optional)
docker compose up -d

dotnet run --project src/Runax.Hookpipe.Api
```

### In your own ASP.NET Core app

The engine is the `Runax.Hookpipe` library, which the service above is built on:

```csharp
builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml("config/hookpipe.yaml"));
// ...
app.MapHookpipe();
app.MapHookpipeHealth();
```

The package isn't on NuGet yet; reference `src/Runax.Hookpipe` until it is.

### Test

```bash
curl -X POST http://localhost:8080/webhooks/test-webhook \
  -H "Content-Type: application/json" \
  -d '{"hello":"world"}'
```

## Config

Define brokers and webhooks in `config/hookpipe.yaml`. Webhooks publish to targets written
`broker:topic`:

```yaml
version: 2

brokers:
  rabbit:
    type: rabbitmq
    settings:
      uri: ${RABBITMQ_URL}
      exchange_name: webhooks

webhooks:
  github:                                   # POST /webhooks/github
    validate:
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
    message:
      headers: [X-GitHub-Event, X-GitHub-Delivery]
    route:
      - when: { header: X-GitHub-Event, eq: ping }
        to: none                            # acknowledge and drop
      - when: { jsonpath: "$.action", in: [opened, reopened] }
        to: rabbit:github.opened
      - to: rabbit:github.events
```

The loader is strict: unknown keys, missing secrets, and unknown brokers fail startup with the
config path of each problem. See [Configuration](docs/configuration.md) for the full reference,
and [Upgrading](docs/upgrading.md) to move a v1 file over.

## Hot-reload

Hookpipe watches the config file and reloads automatically when it changes. Validators, routes, targets, message config, and allowed methods change without a restart. See [Configuration](docs/configuration.md#hot-reload) for details.

## Brokers

| Broker             | Type               | Topic means             |
| ------------------ | ------------------ | ----------------------- |
| Stdout             | `stdout`           | —                       |
| HTTP relay         | `http`             | URL path (optional)     |
| RabbitMQ           | `rabbitmq`         | Routing key             |
| Kafka              | `kafka`            | Topic                   |
| SQS                | `sqs`              | Queue (mapped to a URL) |
| SNS                | `sns`              | Topic (mapped to an ARN)|
| EventBridge        | `eventbridge`      | Detail type             |
| Redis Streams      | `redis`            | Stream key              |
| Google Pub/Sub     | `google-pubsub`    | Topic ID                |
| Azure Service Bus  | `azure-servicebus` | Queue or topic          |
| Azure Event Hubs   | `azure-eventhubs`  | Event hub               |

See [Brokers](docs/brokers.md) for details.

## Validators

| Validator      | Type            | Use case                        |
| -------------- | --------------- | ------------------------------- |
| Bearer token   | `bearer`        | Coolify, custom webhooks        |
| HMAC-SHA256    | `hmac-sha256`   | GitHub, Shopify                 |
| Stripe         | `stripe-v1`     | Stripe webhooks                 |
| API key        | `api-key`       | Custom header + key             |
| IP allowlist   | `ip-allowlist`  | Restrict by IP/CIDR             |

See [Validators](docs/validators.md) for details.

## Message envelope

Every webhook is wrapped in a standardized envelope:

```json
{
    "id": "13979c7e-108c-48b1-aac5-ff9220a3875b",
    "webhookId": "github",
    "receivedAt": "2026-05-07T16:23:55.644839+00:00",
    "method": "POST",
    "path": "/webhooks/github",
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
| `SEQ_URL`                    | Seq server URL             | — (disabled)           |
| `LOKI_URL`                   | Grafana Loki URL           | — (disabled)           |

The request body limit is a config key, not an env var: `server.max_body_size` for the whole
process, and `max_body_size` on a webhook for just that route.

Broker connection strings and secrets are whatever variables your config references with
`${…}`. See [Configuration](docs/configuration.md) for all env vars and [Logging](docs/logging.md)
for log destinations.

## Endpoints

| Method | Path              | Description                                   |
| ------ | ----------------- | --------------------------------------------- |
| `GET`  | `/health`         | Liveness check                                |
| `GET`  | `/health/brokers` | Live probe of every broker                    |
| `GET`  | `/metrics`        | Prometheus metrics                            |
| config | `/webhooks/*`     | Webhooks from config (prefix configurable)    |

See [Metrics](docs/metrics.md) for available metrics and Prometheus scrape config.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
