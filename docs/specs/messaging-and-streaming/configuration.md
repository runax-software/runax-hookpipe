# 5. Configuration v2

Part of [Spec: Runax.Hookpipe: runax-messaging core, streaming, library and service](README.md).

## 5.1 Principles

- **Three things to define.**
  - `brokers` are the messaging systems Hookpipe connects to.
  - `webhooks` receive HTTP calls and publish them **to** brokers.
  - `streams` read **from** brokers and push to clients. With WebSockets, later, they also send
    client messages back **to** brokers.

  There is no separate sink or destination section. A webhook or stream names its broker and
  topic inline as a target, such as `rabbit:github.push` ([§5.5](#55-targets)).
- **Fixed URL prefixes.** Every webhook is served at `/webhooks/<path>` and every stream at
  `/streams/<path>`. Both prefixes can be changed in `server.paths`. `<path>` defaults to the
  webhook's or stream's ID.
- **One model, three sources.**
  - **A YAML file.** The service reads `config/hookpipe.yaml`, overridable with
    `HOOKPIPE_CONFIG_PATH`. The library takes any path.
  - **An `IConfiguration` section,** such as `appsettings.json`, library only
    ([§5.12](#512-appsettingsjson-and-other-iconfiguration-sources)).
  - **Code** ([§11.4](packaging.md#114-the-library-api)).

  All three produce the same model and go through the same checks.
- **`version: 2`** is required at the top. v1 files aren't supported: Hookpipe refuses to start
  and points to the upgrade guide ([§5.10](#510-upgrading-from-v1)).
- **Maps keyed by ID** instead of lists with `id:`. IDs use letters, digits, `.`, `_`, and `-`.
  They're compared case-insensitively, as `IConfiguration` does, so `GitHub` and `github` in the
  same section are a duplicate, and duplicates are a load error.
- **Strict.** Unknown keys are errors, reported with their path
  (`webhooks.github.validate[0].secrett: unknown key`).
- **`${…}` is resolved at load; `{…}` is resolved per message.** Load-time interpolation fills
  in secrets and environment values. Single-brace templates are filled in for each message from
  path parameters, metadata, the topic, or the JSON body.
- **Formats.** Durations are written `500ms`, `5s`, `2m`, or `1h`. Sizes are written `512KB` or
  `10MB`.
- **Key names.** Property names match ignoring case and underscores, so `max_body_size`,
  `MaxBodySize`, and `maxBodySize` are the same key. YAML is documented in snake_case, as in v1;
  `appsettings.json` reads naturally in PascalCase. IDs, header names, metadata keys, and
  dictionary keys in broker `settings` are never rewritten.

## 5.2 Full example

```yaml
version: 2

server:
  instance_id: ${HOSTNAME:-hookpipe-local}
  shutdown_timeout: 30s
  max_body_size: 25MB                     # service only; replaces HOOKPIPE_MAX_BODY_SIZE_MB
  paths:
    webhooks: /webhooks                   # the default: every webhook lives under it
    streams: /streams                     # the default: every stream lives under it

brokers:
  rabbit:
    type: rabbitmq
    settings:                             # bound onto runax-messaging's RabbitMqConfig
      uri: ${RABBITMQ_URL}
      exchange_name: webhooks
    publish:
      queue: { capacity: 1000, when_full: wait, enqueue_timeout: 2s }
      batch: { max_size: 50, max_delay: 5ms }
      retry: { max_retries: 3, delay: 2s, backoff: exponential, factor: 2, max_delay: 30s }
  kafka:
    type: kafka
    settings:
      bootstrap_servers: ${KAFKA_BROKERS}
    publish:
      workers: 8                          # parallel; each topic stays in order by default
      batch: { max_size: 100, max_delay: 5ms }
  console:
    type: stdout                          # Hookpipe-local, publish-only

webhooks:
  github:                                 # POST /webhooks/github (the path defaults to the ID)
    validate:                             # all must pass
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
      - type: ip-allowlist
        allow: [192.30.252.0/22, 185.199.108.0/22, 140.82.112.0/20]
    message:
      headers: [X-GitHub-Event, X-GitHub-Delivery]    # true | false | [list]
    delivery: { ack: delivered, accept_when: any, timeout: 10s }
    route:
      - when: { header: X-GitHub-Event, eq: ping }
        to: []                            # acknowledge and drop
      - when: { header: X-GitHub-Event, eq: push }
        to: rabbit:github.push            # broker:topic
      - to: rabbit:github.other           # the last rule has no `when`

  stripe:                                 # POST /webhooks/stripe
    validate:
      - type: stripe-v1
        header: Stripe-Signature
        secret: ${STRIPE_WEBHOOK_SECRET}
    route:                                # routing on the JSON body
      - when: { jsonpath: "$.type", in: [invoice.paid, checkout.session.completed] }
        to: { broker: kafka, topic: payments, order_by: "{$.data.object.customer:-unknown}" }
      - when:
          all:
            - { jsonpath: "$.type", starts_with: "charge." }
            - { jsonpath: "$.data.object.amount", gte: 100000 }
        to: [kafka:payments, rabbit:alerts.large-charge]
      - to: []                            # everything else: acknowledge and drop

  ingest:
    path: ingest/{source}                 # POST /webhooks/ingest/{source}
    rate_limit: { requests: 100, window: 60s }
    message:
      metadata: { source: "{source}" }
    delivery: { ack: enqueued }
    to: ["kafka:ingest.{source}", console]   # no route: every request goes to both

streams:
  github-live:                            # GET /streams/github-live
    from: [rabbit:github.push, rabbit:github.other]
    auth:
      - type: bearer
        token: ${STREAM_TOKEN}
        query_parameter: access_token     # lets browser EventSource authenticate
    cors:
      allowed_origins: [https://dash.example.com]
    event: { name: "{topic}", data: body }
    buffer: { inbox: 1024, replay: 500, per_client: 256, slow_client: disconnect }
    limits: { max_clients: 5000, max_connection_age: 1h }
    protocols:
      sse: { heartbeat: 15s, retry: 3s }

  ingest-by-source:
    path: ingest/{source}                 # GET /streams/ingest/{source}
    from: [kafka:ingest.billing, kafka:ingest.crm]
    filter: { jsonpath: "$.metadata.source", eq: "{source}" }   # SSE by default

  paid-invoices:
    path: payments/paid                   # GET /streams/payments/paid
    from: kafka:payments
    filter: { jsonpath: "$.body.data.object.status", eq: paid }   # $.body: inside Hookpipe's envelope
```

## 5.3 `server`

| Key | Description | Default |
| --- | --- | --- |
| `instance_id` | Unique per replica; used for broadcast subscriptions ([§3.7](architecture.md#37-every-replica-must-see-every-message)) | `${HOSTNAME}` |
| `shutdown_timeout` | Time to drain publish queues and close streams | `30s` |
| `max_body_size` | Kestrel's body limit for the whole process, such as `25MB`. Service only ([§11.5](packaging.md#115-brokers-in-library-mode)). | Kestrel's default |
| `paths.webhooks` | Prefix every webhook is served under | `/webhooks` |
| `paths.streams` | Prefix every stream is served under | `/streams` |

**About the prefixes:**

- A prefix may be `/`, for example to keep the URLs providers already have when upgrading
  from v1 ([§5.10](#510-upgrading-from-v1)).
- Full routes must be unique, and none may shadow the service's own routes: `/health`,
  `/health/*`, and `/metrics`.

## 5.4 `brokers`

| Key | Description | Default |
| --- | --- | --- |
| `type` | `rabbitmq`, `kafka`, `sqs`, `sns`, `redis`, `google-pubsub`, `azure-servicebus`, `azure-eventhubs` (runax-messaging transports), or `stdout`, `http`, `eventbridge` (Hookpipe-local, publish-only). The service registers all of them. The library registers only `stdout` and `http`; the rest need `AddBrokerType` ([§11.5](packaging.md#115-brokers-in-library-mode)). | required, unless `bus` is set |
| `bus` | Library mode only, instead of `type` and `settings`: use the app's existing runax-messaging bus with this name. Hookpipe doesn't create or reconfigure it. | — |
| `settings` | Deserialized straight into the transport's `TransportConfig` subclass: snake_case keys map to its properties (`exchange_name` → `ExchangeName`). Dictionary keys such as topic names are left alone. runax-messaging's DataAnnotations validation runs at startup, and its errors are reported with the config path. | — |
| `publish.workers` | Publish workers for this broker, each with its own queue ([§3.3](architecture.md#33-publish-workers-one-per-broker-by-default)) | `1` |
| `publish.order_by` | Template that picks the worker. The same key always gets the same worker, so it stays in order. `none` sends each item to the least-loaded worker, with no ordering. A target can override it. | `{topic}` |
| `publish.queue.capacity` | Bounded queue size, per worker | `1000` |
| `publish.queue.when_full` | `wait` or `reject` | `wait` |
| `publish.queue.enqueue_timeout` | How long `wait` waits before a 503 | `2s` |
| `publish.batch.max_size` | Most items per publish | `1` |
| `publish.batch.max_delay` | How long to wait for more items once one is in hand | `0ms` |
| `publish.retry.max_retries` | Retries after the first attempt | `3` with a `retry` block; no retry without one, as in v1 |
| `publish.retry.delay` | First retry delay | `2s` |
| `publish.retry.backoff` | `constant`, `linear`, or `exponential` | `exponential` |
| `publish.retry.factor` | Exponential factor, now honored | `2` |
| `publish.retry.max_delay` | Cap on any single delay | `30s` |
| `publish.retry.jitter` | Randomize delays | `true` |
| `subscribe.mode` | `broadcast` or `shared`. Matters only when a stream reads from this broker ([§3.7](architecture.md#37-every-replica-must-see-every-message)). | `broadcast` |

**Each broker with a `type` becomes one runax-messaging bus,** named `hookpipe:{broker id}`.
Its `BusMode` comes from how it's used:

- `PublishOnly` when only webhooks and stream `to` targets use it.
- `ConsumeOnly` when only stream `from` targets use it.
- `PublishAndConsume` when both do.

A broker with `bus` keeps the app's mode, and the loader checks that its uses fit that mode.

## 5.5 Targets

A target names a broker and a topic. Webhooks use targets in `to`, and streams use them in
`from` and `to`. A target is written one of three ways:

| Form | Example | Use |
| --- | --- | --- |
| `broker:topic` | `rabbit:github.push` | The usual case. Splits at the first `:`. |
| `broker` | `console` | Brokers without topics (`stdout`, and `http` with no path) |
| An object | `{ broker: kafka, topic: payments, order_by: "{$.customer.id}" }` | When you need per-target settings |

**Object keys:**

| Key | Description | Where |
| --- | --- | --- |
| `broker` | Broker ID | Everywhere |
| `topic` | A topic template ([§5.7](#57-conditions-jsonpath-and-templates)). On `http` brokers it's appended to the URL as a path (`relay:orders` POSTs to `<url>/orders`). | Everywhere; `from` topics must be exact (wildcards need P3) |
| `partition_key` | Template for the broker's partition or ordering key (needs P2) | Publishing |
| `order_by` | Overrides the broker's `publish.order_by` for this target | Publishing |

`to` and `from` take one target or a list. `to: none` (or `to: []` in YAML) means "acknowledge
and publish nothing". `none` is reserved and can't be a broker ID.
Inside a flow list (`[…]`), quote targets that contain `{…}`, as YAML requires:
`["kafka:ingest.{source}", console]`.

## 5.6 `webhooks`

| Key | Description | Default |
| --- | --- | --- |
| `path` | Route under `server.paths.webhooks`, may contain `{params}`. A leading `/` is allowed and means the same. | The webhook ID |
| `methods` | Allowed methods | `[POST]` |
| `validate` | List of validators, all must pass. `hmac-sha256` (`header`, `secret`), `stripe-v1` (`header`, `secret`), `bearer` (`token`), `api-key` (`header`, `key`), `ip-allowlist` (`allow`: a list, or a comma-separated string so `${ALLOWED_IPS}` works) | none |
| `rate_limit` | `requests`, `window` | none |
| `max_body_size` | Body limit for this webhook, such as `10MB`; 413 above it. Applied per request, so it works in library mode too. | The host's limit |
| `message.body` | Include the body | `true` |
| `message.headers` | `true`, `false`, or a list of names | `false` |
| `message.metadata` | Static values or templates | none |
| `delivery.ack` | `delivered` or `enqueued` ([§3.4](architecture.md#34-ingress-http--brokers)) | `delivered` |
| `delivery.accept_when` | `any` or `all` targets must succeed | `any` |
| `delivery.timeout` | Upper bound on the `delivered` wait | `30s` |
| `to` | Targets for every request. Use either `to` or `route`. | — |
| `route` | Ordered rules `{ when?, to }`; the first match wins. The last rule must have no `when`. | — |

The envelope's `webhookId` field and the `hookpipe.webhook.id` header carry the webhook ID. In v1
they were `endpointId` and `hookpipe.endpoint.id` ([§13](breaking-changes.md)).

## 5.7 Conditions, JSONPath, and templates

Conditions decide where a webhook goes (`route[].when`) and which events a stream client
receives (`streams.<id>.filter`). Templates build per-message values, such as topics, ordering
keys, and event names, from the same data.

### Conditions

A condition names one source and one operator:

```yaml
- { jsonpath: "$.status", eq: paid }
- { jsonpath: "$.data.object.amount", gte: 100000 }
- { jsonpath: "$.type", in: [invoice.paid, checkout.session.completed] }
- { header: X-GitHub-Event, eq: push }
- { path: source, ne: test }
```

| Source | Reads |
| --- | --- |
| `jsonpath` | The JSON body, through an RFC 9535 JSONPath. On webhooks that's the request body. On streams it's the message body without `__runax`. For Hookpipe's own envelopes the payload sits under `$.body`, so `$.body.status`. |
| `header` | A request header on webhooks, a message header on streams. The name is case-insensitive. |
| `path` | A path parameter of the webhook or stream route |

| Operator | Matches when | Notes |
| --- | --- | --- |
| `eq: v` | The value equals `v` | When `v` is a number or boolean, the comparison is by value: `100` matches `100` and `100.0`. Anything else compares as text, and a JSON number's text is its literal, which keeps v1's string comparison working. Values from `IConfiguration` have no type ([§5.12](#512-appsettingsjson-and-other-iconfiguration-sources)). |
| `ne: v` | The value doesn't equal `v` | False when nothing is selected. Use `not: { … eq: v }` for "missing or different". |
| `in: [a, b]` | The value equals one of the list | Compares like `eq` |
| `not_in: [a, b]` | The value equals none of the list | False when nothing is selected |
| `gt`, `gte`, `lt`, `lte` | Numeric comparison | JSON numbers only. A string or missing value doesn't match. |
| `contains: v` | The text contains `v` | Case-insensitive, as in v1 |
| `starts_with: v`, `ends_with: v` | The text starts or ends with `v` | Case-insensitive |
| `regex: p` | The .NET regex `p` matches | Case-sensitive. Compiled at load, with a 100 ms match timeout. |
| `exists: true` / `exists: false` | At least one non-null value is selected / none is | — |

- **Several selected values.** When a JSONPath selects several values (`$.items[*].sku`), the
  condition holds if any of them matches. For "every item", negate the opposite: "every `qty` is
  above 0" is `not: { jsonpath: "$.items[*].qty", lte: 0 }`.
- **JSONPath filter selectors** cover what the operators don't:
  `{ jsonpath: "$.items[?@.qty > 10 && @.sku == 'A1']", exists: true }`.
- **Placeholders in values.** Values can contain path-parameter placeholders:
  `{ jsonpath: "$.tenantId", eq: "{tenant}" }`.
- **Non-JSON bodies.** When the body isn't JSON, `jsonpath` conditions don't match, except
  `exists: false`. It's logged at debug level, as in v1.

### Combining conditions

`when` and `filter` take one condition, a list (all must match), or an `all`, `any`, or `not`
node. Nodes can nest up to 8 levels deep. Route rules are checked in order, and the first match
wins.

```yaml
route:
  - when:                                         # a list: all of them
      - { jsonpath: "$.type", eq: invoice.paid }
      - { jsonpath: "$.livemode", eq: true }
    to: kafka:payments
  - when:
      any:
        - { jsonpath: "$.data.object.amount", gte: 100000 }
        - { header: X-Priority, eq: high }
    to: [kafka:payments, rabbit:alerts]
  - when:
      not: { jsonpath: "$.status", in: [paid, refunded] }
    to: kafka:pending
  - to: kafka:other
```

### JSONPath

- **Standard.** Syntax and semantics follow RFC 9535, implemented with
  [JsonPath.Net](https://www.nuget.org/packages/JsonPath.Net). It's built on System.Text.Json,
  and 3.x targets net10.0. Dot and bracket notation, wildcards, slices, recursive descent
  (`..`), and filter selectors (`?@.price > 10`) all work.
- **Parsed at load.** Every path is parsed when the config loads. A syntax error fails the load
  and reports the config path of the bad expression.
- **One parse per message.** The body is parsed into a `JsonNode` once. Every route rule,
  template, and filter that needs it shares that copy. Messages whose rules and targets use no
  JSONPath skip the parse.
- **v1 dot paths.** v1's `$.event.type` is already valid JSONPath. A path written without `$.`
  (`event.type`) needs it added, and a segment that isn't a plain identifier needs bracket
  notation (`$['x-id']`).

### Templates

`{…}` fills in a value per message:

| Placeholder | Value |
| --- | --- |
| `{<name>}` | A path parameter, as in v1's metadata (`{source}`, `{tenant}`). Path parameters may not use the reserved names below. |
| `{webhook_id}` | The webhook's ID |
| `{topic}` | The target topic on webhooks (after rendering), the source topic on streams |
| `{metadata.<key>}` | A value from `message.metadata` |
| `{header.<name>}` | A request header, or a message header on streams |
| `{$<jsonpath>}` | One value selected from the body, for example `{$.customer.id}` |

**Where templates are used:** target `topic`, `partition_key`, and `order_by`, the broker's
`publish.order_by`, `message.metadata` values, and `streams.*.event.name`.

**JSONPath templates:**

- A JSONPath template must select exactly one scalar.
- `{$.status:-unknown}` supplies a default when the path selects nothing. It's the same `:-` as
  `${VAR:-default}`.
- With no default, an unresolved or multi-valued template fails that message for that target.
  The failure is logged with the envelope ID and counted as `template_unresolved`. Other targets
  are unaffected.

**Routing on body values works two ways:**

- **Route rules with `jsonpath` conditions,** which pick from a fixed set of targets.
- **Templates in the topic itself,** such as `kafka:payments.{$.type}`. A rendered topic value
  must stay within `[A-Za-z0-9._-]`, or the message fails for that target
  (`template_invalid`). That rules out RabbitMQ wildcards and odd Kafka topic names. A
  body-derived topic still lets the caller choose among topic names, so prefer route rules
  unless the value is signed and comes from a small known set.

## 5.8 `streams`

| Key | Description | Default |
| --- | --- | --- |
| `path` | Route under `server.paths.streams`, may contain `{params}` | The stream ID |
| `from` | One target or a list ([§5.5](#55-targets)). Topics are exact, and brokers must be able to consume. Targets on several brokers are merged into one stream. | required |
| `to` | Where client messages go, for bidirectional protocols ([§4.8](streaming.md#48-future-protocols-websockets-and-webrtc)). One target, published through the broker's publish workers like a webhook's. Rejected while only SSE is enabled. | none |
| `filter` | One condition, a list, or an `all`/`any`/`not` node ([§4.5](streaming.md#45-server-side-filters)) | none |
| `auth` | `bearer`, `api-key`, or `ip-allowlist`. Each token type takes an optional `query_parameter`. Body-signature validators are rejected. | none |
| `cors.allowed_origins`, `cors.allow_credentials` | CORS for browser clients | none |
| `event.name`, `event.data` | [§4.4](streaming.md#44-what-a-client-receives) | `"{topic}"`, `body` |
| `buffer.inbox` | Bounded inbox between consumer and worker | `1024` |
| `buffer.replay` | Events kept for resume (`0` turns resume off) | `256` |
| `buffer.per_client` | Outbox size per subscriber | `256` |
| `buffer.slow_client` | `disconnect` or `drop_oldest` | `disconnect` |
| `limits.max_clients` | 503 above this | `10000` |
| `limits.max_connection_age` | Force a reconnect after this long | none |
| `protocols.sse` | `heartbeat`, `retry`. Without a `protocols` block a stream serves SSE with these defaults; `sse: false` turns it off once another protocol exists. | `15s`, `3s` |
| `local_delivery`, `dedupe.*`, `debounce.*` | In-memory delivery of this replica's own publishes, duplicate dropping, and merging events per key. An improvement in Phase 6 ([§4.9](streaming.md#49-improvement-local-delivery-dedupe-and-debounce)). | `after_publish`; dedupe on by message ID; debounce off |

## 5.9 Interpolation and load-time checks

**Interpolation:**

- `${VAR}` is the environment variable `VAR`. A missing variable is a load error that names the
  variable, never its value.
- `${VAR:-default}` uses `default` when `VAR` is unset or empty.
- `${file:/run/secrets/name}` reads a file and trims the trailing newline (Docker and Kubernetes
  secrets).
- `$${` is a literal `${`.
- Interpolated values count as secrets: they never appear in logs or validation messages.

**Checked at load**, so these failures happen at startup or reload instead of on a request:

- Every target names a known broker. Publish targets don't use consume-only broker types, and
  `from` targets don't use publish-only ones (`stdout`, `http`, `eventbridge`).
- Each webhook has exactly one of `to` and `route`, and a route's last rule has no `when`.
- Full routes (prefix + path) are unique and don't shadow the service's own routes. Every
  placeholder in a template refers to a path parameter or metadata key that exists.
- Every JSONPath parses (RFC 9535), every regex compiles, and operator values have the right
  type (`gt` needs a number, `in` needs a list).
- Validator types are known and their fields are complete. Stream `auth` uses token-style types
  only.
- Broker `settings` bind and pass runax-messaging's validation.
- Every broker `type` is registered. Every `bus` refers to an existing bus whose mode fits how
  it's used.
- Broadcast is possible for every broker a stream reads from ([§3.7](architecture.md#37-every-replica-must-see-every-message)).
- Features gated on runax-messaging changes (`partition_key`, wildcard topics) are rejected with
  a clear message until the referenced package version supports them.

## 5.10 Upgrading from v1

v2 is a breaking release. Hookpipe doesn't read v1 files: there's no translator and no
compatibility mode. A file without `version: 2` stops startup with an error that names the file
and links this section:

```
[Hookpipe.Config] 'config/hookpipe.yaml' has no 'version: 2'. Config format v1 is not
supported; see the upgrade guide: docs/upgrading.md
```

Rewrite the file by hand with the tables below. [§13](breaking-changes.md) lists everything
else that changes.

**Before you start:**

- **To keep your URLs**, set `server.paths.webhooks: /` and keep each v1 path as written.
  Providers already have those URLs configured. Moving them under `/webhooks` can be a later,
  deliberate step.
- **Turn v1 sinks into brokers.** A v1 sink was a connection plus a destination. In v2 the
  connection becomes a broker, and the destination becomes the topic in a target. Several v1
  sinks on the same connection can share one broker, with a different topic in each target.

| v1 | v2 |
| --- | --- |
| `endpoints: [{ id: x, path: /a/b, … }]` | `webhooks: { x: { path: a/b, … } }`, plus `server.paths.webhooks: /` to keep the URL |
| `sink: a` / `sinks: [a, b]` | `to: [a:<topic of a>, b:<topic of b>]` |
| `routing: [{ match: { header: H, operator: O, pattern: P }, sinks }]` | `route: [{ when: { header: H, <op>: P }, to: […] }]` |
| `routing: [{ match: { body: "$.a.b", … }, sinks }]` | `when: { jsonpath: "$.a.b", <op>: P }` |
| Operators `value`, `contains`, `starts_with`, `regex`, `exists` | `eq`, `contains`, `starts_with`, `regex`, `exists: true` |
| `routing: [{ default: true, sinks }]` | The final `route` rule, without `when` |
| No default rule | Add a final `{ to: … }` rule with the endpoint's `sinks`. v1 fell back to them implicitly; v2 has no fallback. |
| `validation.signature { header, secret_env, algorithm }` | `validate: [{ type: <algorithm>, header, secret: ${<secret_env>} }]` |
| `validation.auth { type: bearer, token_env }` | `validate: [{ type: bearer, token: ${<token_env>} }]` |
| `validation.auth { type: api-key, header, token_env }` | `validate: [{ type: api-key, header, key: ${<token_env>} }]` |
| `validation.auth { type: ip-allowlist, token_env }` | `validate: [{ type: ip-allowlist, allow: ${<token_env>} }]` |
| `message.include_body` | `message.body` |
| `message.include_headers` + `header_filter` | `message.headers: true` or `[list]` |
| `rate_limit.window_seconds: 60` | `rate_limit.window: 60s` |
| `sinks: [{ id, type, settings, retry }]` | `brokers.<id>` with `type`, `settings`, and `publish.retry` |
| `retry.delay_seconds: 2` | `publish.retry.delay: 2s` |
| `retry.backoff_multiplier` (ignored) | `publish.retry.factor`, now applied. v1 always behaved as if it were `2`. |

**Per sink type**, as a broker and the topic its targets use:

| v1 `type` | v2 broker `type` | `settings` | Topic in targets |
| --- | --- | --- | --- |
| `stdout` | `stdout` | — | — |
| `rabbitmq` | `rabbitmq` | `uri: ${<url_env>}`, `exchange_name: <exchange>` | `<routing_key>` |
| `kafka` | `kafka` | `bootstrap_servers: ${<brokers_env>}` | `<topic>`, plus `partition_key: "{webhook_id}"` once P2 ships |
| `http` | `http` | `url: ${<url_env>}`, `timeout: <timeout_seconds>s` | — |
| `sqs` | `sqs` | `region`, `service_url`, `topic_queue_url_map: { <sink id>: ${<queue_url_env>} }` | `<sink id>` |
| `sns` | `sns` | `region`, `service_url`, `topic_arn_map: { <sink id>: ${<topic_arn_env>} }` | `<sink id>` |
| `eventbridge` | `eventbridge` | `event_bus_name: ${<event_bus_env>}`, `source`, `region`, `service_url` | `<detail_type>` (default `webhook`), which becomes the event's `DetailType` |
| `redis-stream` | `redis` | `configuration: ${<connection_env>}` | `<stream_key>` |
| `google-pubsub` | `google-pubsub` | `project_id: ${<project_id_env>}` | `${<topic_id_env>}` |
| `servicebus` | `azure-servicebus` | `connection_string: ${<connection_string_env>}` | `<queue_or_topic>` |
| `eventhub` | `azure-eventhubs` | `connection_string: ${<connection_string_env>}` | `<event_hub_name>` |

v1 settings that relied on a default env var name (for example `url_env` defaulting to
`RABBITMQ_URL`) become an explicit `${RABBITMQ_URL}`.

## 5.11 Hot reload

| Change | Takes effect |
| --- | --- |
| `webhooks.*`: `methods`, `validate`, `message`, `to`, `route`, `delivery`, `max_body_size` | Live, on the next request (as v1) |
| `brokers.*.publish`: `batch`, `retry` | Live, on each worker's next batch |
| Target topics and keys, in `to` and `route` | Live, on the next request |
| `streams.*`: `filter`, `event`, `buffer.slow_client`, `protocols.sse.heartbeat` | Live. Filters without placeholders apply to the next event; those with placeholders apply to new connections. |
| Adding or removing webhooks, streams, or brokers; changing a `path`, `server.paths`, `brokers.*.type`, `settings`, `bus`, `publish.workers`, `publish.order_by`, `publish.queue`, `subscribe`, `streams.*.from`, buffer sizes, `rate_limit`, or anything else under `server` | Restart |

**Where reloads come from.** For a YAML file, a file watcher with a 500 ms debounce, as in v1.
For an `IConfiguration` section, its change token: `appsettings.json` with `reloadOnChange`,
Azure App Configuration refresh, and so on, debounced the same way. Code definitions never
reload.

**Rejected reloads.** v1 accepts a reload that adds a sink and then fails requests that route to
it. v2 compares the new file's structure with the running one, applies the live changes, and
logs `restart required for: brokers.audit (added)`. If a live change refers to something that
doesn't exist at runtime, such as a route to the new broker, the whole reload is rejected and
the previous config stays in force.

## 5.12 `appsettings.json` and other `IConfiguration` sources

In library mode, the whole definition can live in an `IConfiguration` section, next to the rest
of the app's settings:

```csharp
builder.Services.AddHookpipe(hookpipe =>
    hookpipe.LoadConfiguration(builder.Configuration.GetSection("Hookpipe")));
```

```json
{
  "Hookpipe": {
    "Server": { "Paths": { "Webhooks": "/hooks" } },
    "Brokers": {
      "rabbit": { "Bus": "main" }
    },
    "Webhooks": {
      "github": {
        "Validate": [
          { "Type": "hmac-sha256", "Header": "X-Hub-Signature-256", "Secret": "${GITHUB_WEBHOOK_SECRET}" }
        ],
        "Route": [
          { "When": { "Header": "X-GitHub-Event", "Eq": "ping" }, "To": "none" },
          { "When": { "Header": "X-GitHub-Event", "Eq": "push" }, "To": "rabbit:github.push" },
          { "To": "rabbit:github.other" }
        ]
      }
    },
    "Streams": {
      "paid-invoices": {
        "From": "rabbit:payments",
        "Filter": { "JsonPath": "$.body.data.object.status", "Eq": "paid" }
      }
    }
  }
}
```

**What carries over from YAML:**

- **Same keys.** Property names match ignoring case and underscores, so the JSON above uses
  PascalCase for what YAML writes in snake_case.
- **Same structures.** Lists are JSON arrays, and targets use the same `broker:topic` strings.
- **Same rules.** `${VAR}` interpolation, the load-time checks in
  [§5.9](#59-interpolation-and-load-time-checks), and the hot-reload rules in
  [§5.11](#511-hot-reload) all apply.
- **`Version` may be left out.** v1 never had this source, so there's no older format to
  reject.

**Where `IConfiguration` differs, and what Hookpipe does about it:**

| Difference | Handling |
| --- | --- |
| **Every value is text.** JSON `100` and `"100"` arrive as the same string. | `eq`, `ne`, `in`, and `not_in` match an untyped value when the text is equal, or when both sides parse as the same number (`100` and `100.0`) or the same boolean. Numeric operators (`gt`, `gte`, `lt`, `lte`) need a value that parses as a number, or the load fails. |
| **Empty objects and arrays vanish.** `{}` and `[]` produce no keys at all. | Nothing needs an empty value. A stream without `Protocols` serves SSE with defaults, and "publish nothing" is written `"To": "none"`. |
| **Arrays are flattened to numbered keys** (`Validate:0:Type`). | A section whose children are all `0`, `1`, `2`, … is read as a list. |
| **Keys are case-insensitive.** | IDs are compared case-insensitively in every source, so nothing changes meaning between YAML and JSON. |
| **Layering.** `appsettings.{Environment}.json`, environment variables, user secrets, Key Vault, and so on all merge into the section. | That merge is `IConfiguration`'s own, before Hookpipe reads anything. It's the natural way to override one value per environment, for example `Hookpipe__Webhooks__github__Validate__0__Secret` as an environment variable. |
| **Errors point at keys, not lines.** | Messages name the configuration path (`Hookpipe:Webhooks:github:Validate:0:Secrett: unknown key`), never the value. |

**Secrets.** Fields that hold credentials are never logged or echoed in errors, whichever source
they come from. That covers validator `secret`, `token`, and `key`, plus broker `settings`
properties the transport marks as sensitive (passwords, connection strings). The same goes for
interpolated values.

**Combining sources.** A library app can use YAML, `IConfiguration`, and code together:

- Each broker, webhook, and stream must come from exactly one source. The same ID in two sources
  is a load error, not a merge.
- `server` keys may come from any source, but setting the same key in two sources is an error.
- To override values per environment, use `IConfiguration`'s own layering within one source
  instead of splitting an item across sources.

**The service.** The self-hosted service keeps reading YAML only. Overriding YAML values from
environment variables would mean merging across sources, which is out of scope here. `${VAR}`
interpolation inside the YAML file covers the common case.

