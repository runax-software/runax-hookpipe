# 13. Breaking changes

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

v2 is a breaking release, and nothing below has a compatibility mode. There's no config
translator, no alias, and no second name, so v2 carries no code that exists only for v1.
Upgrading is one step: rewrite the config, update downstream consumers and dashboards, and
switch the image. This list is meant to go straight into the release notes.

## 13.1 Configuration

| v1 | v2 | See |
| --- | --- | --- |
| Files without a version | Rejected at startup. `version: 2` is required, and the error links the upgrade guide. | [§5.10](configuration.md#510-upgrading-from-v1) |
| `endpoints`, a list with `id:` | `webhooks`, a map keyed by ID | [§5.6](configuration.md#56-webhooks) |
| `sinks` (connection plus destination) | `brokers` (connection) plus inline targets such as `rabbit:github.push` | [§5.4](configuration.md#54-brokers), [§5.5](configuration.md#55-targets) |
| `sink`, `sinks`, and `routing` | `to`, or `route` with `when` | [§5.6](configuration.md#56-webhooks) |
| Routing operators `value`, `contains`, `starts_with`, `regex`, `exists` | `eq`, `contains`, `starts_with`, `regex`, `exists: true`, plus new operators | [§5.7](configuration.md#57-conditions-jsonpath-and-templates) |
| `match.body` dot paths | `jsonpath` (RFC 9535) | [§5.7](configuration.md#57-conditions-jsonpath-and-templates) |
| No matching rule falls back to the endpoint's `sinks` | No fallback: a `route` must end with a rule that has no `when` | [§5.6](configuration.md#56-webhooks) |
| `validation.signature` or `validation.auth`, one per endpoint | `validate`, a list where all must pass | [§5.6](configuration.md#56-webhooks) |
| `*_env` keys naming environment variables | `${VAR}` interpolation. A missing variable fails startup instead of returning 401 on each request. | [§5.9](configuration.md#59-interpolation-and-load-time-checks) |
| `message.include_body`, `include_headers`, `header_filter` | `message.body`, `message.headers` | [§5.6](configuration.md#56-webhooks) |
| `window_seconds`, `delay_seconds`, `timeout_seconds` | Durations: `60s`, `2s` | [§5.1](configuration.md#51-principles) |
| `retry` per sink, `backoff_multiplier` ignored | `brokers.<id>.publish.retry`, where `factor` is applied | [§5.4](configuration.md#54-brokers) |
| Unknown keys ignored | Unknown keys fail the load | [§5.1](configuration.md#51-principles) |
| `HOOKPIPE_MAX_BODY_SIZE_MB` | `server.max_body_size: 25MB` | [§5.3](configuration.md#53-server) |

## 13.2 URLs and HTTP

| v1 | v2 |
| --- | --- |
| Webhooks at the path you configured | Under `/webhooks/<path>` by default. `server.paths.webhooks: /` keeps v1 URLs. |
| 202 body `{"status":"accepted","endpoint_id":…}` | `{"status":"accepted","webhook_id":…,"message_id":…}` |
| 500 body `all_sinks_failed` | `publish_failed` |
| — | New: 503 `broker_unavailable` when a publish queue is full or Hookpipe is stopping, and 504 `delivery_timeout` |
| `/health/sinks` | `/health/brokers`, plus the new `/health/ready` and `/health/streams` |
| Retries hold the request open for as long as they take | With `ack: delivered`, the request waits at most `delivery.timeout` (default `30s`) |
| A caller disconnecting cancels the publish | Once accepted, a message is published even if the caller goes away |

## 13.3 Messages on the wire

These affect downstream consumers ([§6.3](messaging-integration.md#63-wire-changes-for-downstream-consumers)):

| v1 | v2 |
| --- | --- |
| Envelope field `endpointId` | `webhookId` |
| Header or attribute `hookpipe.endpoint.id` | `hookpipe.webhook.id` |
| Body is exactly the envelope | The envelope plus a `__runax` key (headers, trace context) |
| RabbitMQ `MessageId` property set to the envelope ID | Not set; read the `hookpipe.message.id` header |
| Kafka record key = endpoint ID | No key until runax-messaging P2 ships |
| RabbitMQ exchange per sink | Per broker (`exchange_name`), declared the same way |

## 13.4 Metrics and logs

- **Metrics are renamed.** The full table is in
  [§6.5](messaging-integration.md#65-telemetry). In short:
  - `hookpipe_messages_produced_total` becomes `hookpipe_messages_published_total`.
  - `hookpipe_sink_errors_total` becomes `hookpipe_publish_errors_total`.
  - The `endpoint_id` label becomes `webhook_id`, and `sink_id` becomes `broker_id`.
- **Logger categories** change from `Hookpipe.*` to `Runax.Hookpipe.*`.
  `Logging__LogLevel__Hookpipe` becomes `Logging__LogLevel__Runax`.
- **Log prefixes** change: `[Hookpipe.Endpoint:…]` becomes `[Hookpipe.Webhook:…]`, and
  `[Hookpipe.Sink:…]` becomes `[Hookpipe.Broker:…]`.

## 13.5 Packaging and deployment

- **Image:** `ghcr.io/runax-software/hookpipe` becomes `ghcr.io/runax-software/runax-hookpipe`.
  The old releases, `0.2.0` and `0.2.1`, are copied to the new name, and the old package is
  deleted, so only one package remains.
- **Entry point:** `Hookpipe.API.dll` becomes `Runax.Hookpipe.Api.dll`. That only matters for
  custom images.
- **Code:** projects and namespaces become `Runax.Hookpipe` and `Runax.Hookpipe.Api`. That
  matters for forks and anyone building from source.

## 13.6 What stays the same

- The config path `config/hookpipe.yaml` and `HOOKPIPE_CONFIG_PATH`.
- Port 8080 and the container user.
- `SEQ_URL`, `SEQ_API_KEY`, and `LOKI_URL`.
- The `hookpipe_` metric prefix and the `[Hookpipe.…]` log prefix style.
- The envelope's other fields and its camelCase JSON.
- The validator algorithms and how they check signatures and tokens.
