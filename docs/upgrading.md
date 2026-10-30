# Upgrading from config v1 to v2

Config v2 is a breaking change. Hookpipe doesn't read v1 files: there's no translator and no
compatibility mode. A file without `version: 2` stops startup with:

```
Invalid Hookpipe config 'config/hookpipe.yaml': version: missing. This file looks like config
format v1, which isn't supported; add 'version: 2' and rewrite it with the upgrade guide: …
```

Rewrite the file by hand with the tables below. [configuration.md](configuration.md) describes
v2 in full.

## Before you start

- **To keep your URLs**, set `server.paths.webhooks: /` and keep each v1 path. Providers already
  have those URLs configured. Moving them under `/webhooks` can be a later, deliberate step.
- **Turn sinks into brokers.** A v1 sink was a connection plus a destination. In v2 the
  connection becomes a broker, and the destination becomes the topic in a target. Several v1
  sinks on the same connection can share one broker, with a different topic in each target.
- **Secrets move into the file as `${VAR}`.** v1's `*_env` keys named environment variables. v2
  references them directly, and a missing variable fails startup instead of failing requests.

## Keys

| v1 | v2 |
| --- | --- |
| No version | `version: 2` at the top |
| `endpoints: [{ id: x, path: /a/b, … }]` | `webhooks: { x: { path: a/b, … } }`, plus `server.paths.webhooks: /` to keep the URL |
| `sink: a` / `sinks: [a, b]` | `to: [a:<topic of a>, b:<topic of b>]` |
| `routing: [{ match: { header: H, operator: O, pattern: P }, sinks }]` | `route: [{ when: { header: H, <op>: P }, to: […] }]` |
| `routing: [{ match: { body: "$.a.b", … }, sinks }]` | `when: { jsonpath: "$.a.b", <op>: P }` |
| Operators `value`, `contains`, `starts_with`, `regex`, `exists` | `eq`, `contains`, `starts_with`, `regex`, `exists: true` |
| `routing: [{ default: true, sinks }]` | The final `route` rule, without `when` |
| No default rule | Add a final `{ to: … }` rule with the endpoint's `sinks`. v1 fell back to them; v2 has no fallback. |
| `validation.signature { header, secret_env, algorithm }` | `validate: [{ type: <algorithm>, header, secret: ${<secret_env>} }]` |
| `validation.auth { type: bearer, token_env }` | `validate: [{ type: bearer, token: ${<token_env>} }]` |
| `validation.auth { type: api-key, header, token_env }` | `validate: [{ type: api-key, header, key: ${<token_env>} }]` |
| `validation.auth { type: ip-allowlist, token_env }` | `validate: [{ type: ip-allowlist, allow: ${<token_env>} }]` (or a list) |
| `message.include_body` | `message.body` |
| `message.include_headers` + `header_filter` | `message.headers: true` or a list of names |
| `rate_limit.window_seconds: 60` | `rate_limit.window: 60s` |
| `sinks: [{ id, type, settings, retry }]` | `brokers.<id>` with `type`, `settings`, and `publish.retry` |
| `retry.delay_seconds: 2` | `publish.retry.delay: 2s` |
| `retry.backoff_multiplier` (ignored) | `publish.retry.factor`, now applied. v1 always behaved as if it were `2`. |
| Unknown keys ignored | Unknown keys fail the load |

Body paths are now [RFC 9535](https://www.rfc-editor.org/rfc/rfc9535) JSONPath. v1's
`$.event.type` is already valid. A path written without `$.` (`event.type`) needs it added.

## Sinks to brokers

Each row shows the v2 broker and the topic its targets use.

| v1 `type` | v2 `type` | `settings` | Topic in targets |
| --- | --- | --- | --- |
| `stdout` | `stdout` | — | — |
| `rabbitmq` | `rabbitmq` | `uri: ${<url_env>}`, `exchange_name: <exchange>` | `<routing_key>` |
| `kafka` | `kafka` | `bootstrap_servers: ${<brokers_env>}` | `<topic>` |
| `http` | `http` | `url: ${<url_env>}`, `timeout: <timeout_seconds>s` | — |
| `sqs` | `sqs` | `region`, `service_url`, `topic_queue_url_map: { <name>: ${<queue_url_env>} }` | `<name>` |
| `sns` | `sns` | `region`, `service_url`, `topic_arn_map: { <name>: ${<topic_arn_env>} }` | `<name>` |
| `eventbridge` | `eventbridge` | `event_bus_name: ${<event_bus_env>}`, `source`, `region`, `service_url` | `<detail_type>` (v1 default `webhook`) |
| `redis-stream` | `redis` | `configuration: ${<connection_env>}` | `<stream_key>` |
| `google-pubsub` | `google-pubsub` | `project_id: ${<project_id_env>}` | `<topic id>` |
| `servicebus` | `azure-servicebus` | `connection_string: ${<connection_string_env>}` | `<queue_or_topic>` |
| `eventhub` | `azure-eventhubs` | `connection_string: ${<connection_string_env>}` | `<event_hub_name>` |

v1 settings that relied on a default variable name (for example `url_env` defaulting to
`RABBITMQ_URL`) become an explicit `${RABBITMQ_URL}`. RabbitMQ's exchange now defaults to
`runax.messaging` instead of the default exchange, so set `exchange_name` if you relied on that.
For Pub/Sub's emulator, set `PUBSUB_EMULATOR_HOST` directly.

## An example

v1:

```yaml
endpoints:
  - id: github-push
    path: /github/push
    validation:
      signature:
        header: X-Hub-Signature-256
        secret_env: GITHUB_WEBHOOK_SECRET
        algorithm: hmac-sha256
    sink: rabbitmq-main
    routing:
      - match: { header: X-GitHub-Event, operator: value, pattern: ping }
        sinks: [stdout-dev]
    message:
      include_headers: true
      header_filter: [X-GitHub-Event]

sinks:
  - id: rabbitmq-main
    type: rabbitmq
    settings:
      url_env: RABBITMQ_URL
      exchange: webhooks
      routing_key: github
    retry:
      max_retries: 3
      delay_seconds: 2
  - id: stdout-dev
    type: stdout
```

v2:

```yaml
version: 2

server:
  paths:
    webhooks: /                  # keep /github/push

brokers:
  rabbit:
    type: rabbitmq
    settings:
      uri: ${RABBITMQ_URL}
      exchange_name: webhooks
    publish:
      retry: { max_retries: 3, delay: 2s }
  console:
    type: stdout

webhooks:
  github-push:
    path: github/push
    validate:
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
    message:
      headers: [X-GitHub-Event]
    route:
      - when: { header: X-GitHub-Event, eq: ping }
        to: console
      - to: rabbit:github        # v1's fallback to the endpoint's sink
```

## Everything else that changes

| v1 | v2 |
| --- | --- |
| 202 body `{"status":"accepted","endpoint_id":…}` | `{"status":"accepted","webhook_id":…,"message_id":…}` |
| 500 body `all_sinks_failed` | `publish_failed` |
| `/health/sinks` | `/health/brokers`, keyed by broker ID |
| Envelope field `endpointId` | `webhookId` |
| Header or attribute `hookpipe.endpoint.id` | `hookpipe.webhook.id` |
| Kafka record key = endpoint ID | The webhook ID |
| `hookpipe_messages_produced_total{endpoint_id, sink_id}` | `hookpipe_messages_published_total{webhook_id, broker_id}` |
| `hookpipe_sink_errors_total{endpoint_id, sink_id}` | `hookpipe_publish_errors_total{webhook_id, broker_id}` |
| `endpoint_id` label on the other metrics | `webhook_id` |
| Log prefixes `[Hookpipe.Endpoint:…]`, `[Hookpipe.Sink:…]` | `[Hookpipe.Webhook:…]`, `[Hookpipe.Broker:…]` |
| Routes matched case-sensitively | ASP.NET Core routing, which ignores case in literal segments |
| A metadata value's `{name}` that isn't a path parameter is kept as written | Every `{name}` must be a path parameter, `{webhook_id}`, `{header.Name}`, or `{$.path}`; write `{{` and `}}` for literal braces |
