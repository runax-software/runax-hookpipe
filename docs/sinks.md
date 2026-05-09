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

### Kafka

Produces messages to a Kafka topic with idempotent delivery and `Acks.All`.

**Type:** `kafka`

```yaml
sinks:
    - id: kafka-events
      type: kafka
      settings:
          brokers_env: KAFKA_BROKERS
          topic: webhook-events
```

**Settings:**

| Key           | Description                              | Default         |
| ------------- | ---------------------------------------- | --------------- |
| `brokers_env` | Env var name holding the broker list     | `KAFKA_BROKERS` |
| `topic`       | Kafka topic to produce to (**required**) | —               |

**Behavior:**

- Uses `Acks.All` and idempotent producer for reliable delivery
- Message key is set to the endpoint ID (ensures ordering per endpoint)
- Message value is the JSON-serialized envelope
- Adds `hookpipe.message.id` and `hookpipe.endpoint.id` as Kafka headers
- Flushes pending messages on shutdown (5s timeout)

### HTTP Relay

Forwards message envelopes as JSON POST requests to a target URL. Useful for relaying webhooks to another service.

**Type:** `http`

```yaml
sinks:
    - id: relay-downstream
      type: http
      settings:
          url_env: HTTP_RELAY_URL
          timeout_seconds: "30"
```

**Settings:**

| Key               | Description                          | Default          |
| ----------------- | ------------------------------------ | ---------------- |
| `url_env`         | Env var name holding the target URL  | `HTTP_RELAY_URL` |
| `timeout_seconds` | HTTP request timeout in seconds      | `30`             |

**Behavior:**

- Sends a `POST` request with `Content-Type: application/json`
- Body is the JSON-serialized message envelope
- Throws on non-success status codes (4xx, 5xx)
- Logs warning on failed relay with status code
- Credentials in URLs are masked in logs

## Planned sinks

- **SQS** — AWS Simple Queue Service

## Creating a custom sink

1. Create a class in `src/Hookpipe.Core/Sinks/` implementing `ISink`
2. Add a case to the `sinkConfig.Type switch` in `Program.cs`
3. Add NuGet packages to `Directory.Packages.props`
4. Add XML docs
5. Add tests

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
