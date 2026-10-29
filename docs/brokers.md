# Brokers

A broker is a messaging system Hookpipe connects to. Each broker in `brokers` gets one
connection at startup, shared by every webhook that publishes to it. A webhook picks the
destination with a target's topic, such as `rabbit:github.push`.

```yaml
brokers:
  rabbit:
    type: rabbitmq
    settings:
      uri: ${RABBITMQ_URL}
      exchange_name: webhooks
```

`settings` keys are strict: an unknown key, or a missing required one, fails the load with its
path (`brokers.rabbit.settings.uri: required`). Use `${…}` for secrets
([configuration.md](configuration.md#interpolation)). Setting names match runax-messaging's
transport configs, so they stay the same when Hookpipe moves onto runax-messaging.

Every published message is the [message envelope](message-envelope.md) as JSON. Where the
broker has message headers or attributes, they carry `hookpipe.message.id` and
`hookpipe.webhook.id`.

## Types

| Type | Topic means | Settings |
| --- | --- | --- |
| `stdout` | No topic | none |
| `http` | A path appended to the URL (optional) | `url`, `timeout` |
| `rabbitmq` | Routing key | `uri`, `exchange_name` |
| `kafka` | Kafka topic | `bootstrap_servers` |
| `sqs` | A key in `topic_queue_url_map` | `topic_queue_url_map`, `region`, `service_url` |
| `sns` | A key in `topic_arn_map` | `topic_arn_map`, `region`, `service_url` |
| `eventbridge` | The event's `DetailType` | `event_bus_name`, `source`, `region`, `service_url` |
| `redis` | Stream key | `configuration` |
| `google-pubsub` | Topic ID | `project_id` |
| `azure-servicebus` | Queue or topic name | `connection_string` |
| `azure-eventhubs` | Event hub name | `connection_string` |

### `stdout`

Writes each envelope to the log as indented JSON. For development.

```yaml
console:
  type: stdout
# target: console
```

### `http`

POSTs each envelope as JSON. A non-success status counts as a failed publish.

```yaml
relay:
  type: http
  settings:
    url: ${HTTP_RELAY_URL}
    timeout: 30s
# targets: relay (POST <url>), relay:orders (POST <url>/orders)
```

| Setting | Description | Default |
| --- | --- | --- |
| `url` | The URL to POST to | required |
| `timeout` | How long a POST may take | `30s` |

Credentials in the URL are masked in logs.

### `rabbitmq`

Publishes persistent JSON messages to an exchange; the topic is the routing key.

```yaml
rabbit:
  type: rabbitmq
  settings:
    uri: ${RABBITMQ_URL}
    exchange_name: webhooks
# target: rabbit:github.push
```

| Setting | Description | Default |
| --- | --- | --- |
| `uri` | AMQP URI, such as `amqp://user:pass@host:5672/vhost` | required |
| `exchange_name` | Exchange to publish to, declared as `topic` and durable at startup | `runax.messaging` |

Messages have `ContentType: application/json`, persistent delivery, and `MessageId` set to the
envelope ID.

### `kafka`

Produces to the target's topic with an idempotent producer and `acks=all`.

```yaml
kafka:
  type: kafka
  settings:
    bootstrap_servers: ${KAFKA_BROKERS}
# target: kafka:webhook-events
```

| Setting | Description | Default |
| --- | --- | --- |
| `bootstrap_servers` | Comma-separated `host:port` list | required |

The record key is the webhook ID, which keeps each webhook's messages in order. Headers carry
`hookpipe.message.id` and `hookpipe.webhook.id`. Pending messages are flushed on shutdown (5 s).

### `sqs`

Sends to SQS queues. The topic picks the queue from `topic_queue_url_map`, and the loader
rejects targets whose topic isn't in the map.

```yaml
sqs:
  type: sqs
  settings:
    region: us-east-1
    topic_queue_url_map:
      orders: ${ORDERS_QUEUE_URL}
      refunds: ${REFUNDS_QUEUE_URL}
# target: sqs:orders
```

| Setting | Description | Default |
| --- | --- | --- |
| `topic_queue_url_map` | Queue URL per topic | required |
| `region` | AWS region | the SDK's resolution (`AWS_REGION`, profile) |
| `service_url` | Custom endpoint, such as LocalStack | — |

Credentials come from the SDK's default chain. With `service_url`, they come from
`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`, or `test`.

### `sns`

Publishes to SNS topics. The topic picks the ARN from `topic_arn_map`.

```yaml
sns:
  type: sns
  settings:
    topic_arn_map:
      orders: ${ORDERS_TOPIC_ARN}
# target: sns:orders
```

| Setting | Description | Default |
| --- | --- | --- |
| `topic_arn_map` | Topic ARN per topic | required |
| `region`, `service_url` | As for `sqs` | — |

### `eventbridge`

Puts events on an EventBridge bus. The topic becomes the event's `DetailType`, and the envelope
JSON is its `Detail`.

```yaml
events:
  type: eventbridge
  settings:
    event_bus_name: ${EVENTBRIDGE_BUS_NAME}
    source: hookpipe
# target: events:webhook
```

| Setting | Description | Default |
| --- | --- | --- |
| `event_bus_name` | Bus name or ARN | required |
| `source` | The events' `Source` | `hookpipe` |
| `region`, `service_url` | As for `sqs` | — |

### `redis`

Appends to Redis streams; the topic is the stream key. Each entry has the fields
`hookpipe.message.id`, `hookpipe.webhook.id`, and `payload` (the envelope JSON).

```yaml
redis:
  type: redis
  settings:
    configuration: ${REDIS_CONNECTION}
# target: redis:webhooks
```

| Setting | Description | Default |
| --- | --- | --- |
| `configuration` | StackExchange.Redis configuration string, such as `localhost:6379` | required |

### `google-pubsub`

Publishes to Pub/Sub topics in one project; the topic is the topic ID. A publisher is created
per topic on first use. Set `PUBSUB_EMULATOR_HOST` to use the emulator.

```yaml
pubsub:
  type: google-pubsub
  settings:
    project_id: ${GOOGLE_CLOUD_PROJECT}
# target: pubsub:webhook-events
```

| Setting | Description | Default |
| --- | --- | --- |
| `project_id` | The project that owns the topics | required |

### `azure-servicebus`

Sends to Service Bus queues or topics; the topic is the queue or topic name. Messages carry the
IDs as application properties, and `MessageId` is the envelope ID.

```yaml
servicebus:
  type: azure-servicebus
  settings:
    connection_string: ${SERVICEBUS_CONNECTION_STRING}
# target: servicebus:webhooks
```

| Setting | Description | Default |
| --- | --- | --- |
| `connection_string` | Service Bus connection string | required |

### `azure-eventhubs`

Sends to Event Hubs; the topic is the event hub name. Events carry the IDs as properties, and
`MessageId` is the envelope ID.

```yaml
eventhubs:
  type: azure-eventhubs
  settings:
    connection_string: ${EVENTHUB_CONNECTION_STRING}
# target: eventhubs:webhooks
```

| Setting | Description | Default |
| --- | --- | --- |
| `connection_string` | Event Hubs namespace connection string | required |

## Health

`GET /health/brokers` probes every broker live on each request (3 s timeout per probe) and
returns 200 with each broker's status, or 503 when any is unhealthy.

## Adding a broker type

1. Create a folder under `src/Runax.Hookpipe/Brokers/` (such as `Kafka/` or `Aws/Sqs/`) with a
   class implementing `IBroker` and a settings class, each in its own file. Name the settings
   properties after the runax-messaging transport config, if there is one.
2. Register it in `BrokerTypes.All` and `BrokerFactory`.
3. Add NuGet packages to `Directory.Packages.props`.
4. Add XML docs and tests.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details.
