# Sinks

Sinks are the message destinations. Each sink implements the `ISink` interface and is registered by its `type` string in the config.

## Available sinks

### Stdout

Writes the message envelope to console as formatted JSON. For development and debugging.

**Type:** `stdout`

```yaml
sinks:
    - id: dev
      type: stdout
```

No settings required.

### RabbitMQ

Publishes messages to a RabbitMQ exchange as persistent JSON messages.

**Type:** `rabbitmq`

```yaml
sinks:
    - id: rabbitmq-main
      type: rabbitmq
      settings:
          url_env: RABBITMQ_URL
          exchange: webhooks
          routing_key: events
```

**Settings:**

| Key           | Description                        | Default                 |
| ------------- | ---------------------------------- | ----------------------- |
| `url_env`     | Env var name holding the AMQP URL  | `RABBITMQ_URL`          |
| `exchange`    | Exchange name                      | `""` (default exchange) |
| `routing_key` | Routing key for published messages | `""`                    |

**Behavior:**

- Declares the exchange as `topic` and `durable` on startup
- Messages are published with `DeliveryMode = Persistent`
- Message `ContentType` is `application/json`
- Message `MessageId` is set to the envelope ID

## Planned sinks

- **SQS** — AWS Simple Queue Service
- **Kafka** — Apache Kafka

## Creating a custom sink

1. Create a class in `src/Hookpipe.Core/Sinks/` implementing `ISink`
2. Add a case to the `sinkConfig.Type switch` in `Program.cs`
3. Add NuGet packages to `Directory.Packages.props`
4. Add XML docs
5. Add tests

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
