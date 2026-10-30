# Configuration

Hookpipe reads one YAML file, `config/hookpipe.yaml` by default (override with
`HOOKPIPE_CONFIG_PATH`). The library takes any path through `hookpipe.LoadYaml(path)`.

The file is config format v2. A file without `version: 2` is refused at startup; see
[upgrading.md](upgrading.md) to move a v1 file over.

## The shape of the file

There are two things to define:

- **`brokers`**: the messaging systems Hookpipe connects to.
- **`webhooks`**: the HTTP routes Hookpipe receives, each publishing to brokers.

A webhook names where it publishes inline, as a **target** such as `rabbit:github.push`
(broker `rabbit`, topic `github.push`). There's no separate sink or destination section.

```yaml
version: 2

server:
  paths:
    webhooks: /webhooks                 # the default; every webhook is served under it

brokers:
  rabbit:
    type: rabbitmq
    settings:
      uri: ${RABBITMQ_URL}
      exchange_name: webhooks
    publish:
      retry: { max_retries: 3, delay: 2s, backoff: exponential, factor: 2, max_delay: 30s }
  kafka:
    type: kafka
    settings:
      bootstrap_servers: ${KAFKA_BROKERS}
  console:
    type: stdout

webhooks:
  github:                               # POST /webhooks/github (the path defaults to the ID)
    validate:                           # all must pass
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
    message:
      headers: [X-GitHub-Event, X-GitHub-Delivery]
    route:
      - when: { header: X-GitHub-Event, eq: ping }
        to: none                        # acknowledge and publish nothing
      - when: { header: X-GitHub-Event, eq: push }
        to: rabbit:github.push
      - to: rabbit:github.other         # the last rule has no `when`

  stripe:                               # POST /webhooks/stripe
    validate:
      - type: stripe-v1
        secret: ${STRIPE_WEBHOOK_SECRET}
    route:
      - when: { jsonpath: "$.type", in: [invoice.paid, checkout.session.completed] }
        to: kafka:payments
      - when:
          all:
            - { jsonpath: "$.type", starts_with: "charge." }
            - { jsonpath: "$.data.object.amount", gte: 100000 }
        to: [kafka:payments, rabbit:alerts.large-charge]
      - to: none

  ingest:
    path: ingest/{source}               # POST /webhooks/ingest/{source}
    rate_limit: { requests: 100, window: 60s }
    message:
      metadata: { source: "{source}" }
    to: [kafka:ingest, console]         # no route: every request goes to both
```

## Rules that apply everywhere

- **Strict.** Unknown keys, missing required values, and references to things that don't exist
  fail the load. Every problem is reported with its path, for example
  `webhooks.github.validate[0].secrett: unknown key`, and one load reports all of them.
- **Maps keyed by ID.** Brokers and webhooks are maps, not lists. IDs use letters, digits, `.`,
  `_`, and `-`, and they're compared ignoring case, so `GitHub` and `github` are a duplicate.
- **Key names** match ignoring case and underscores: `max_retries`, `MaxRetries`, and
  `maxRetries` are the same key. IDs, header names, metadata keys, and map keys inside broker
  `settings` are never changed.
- **Durations** are written `500ms`, `5s`, `2m`, or `1h`.

### Interpolation

`${…}` is filled in when the config loads:

| Written | Becomes |
| --- | --- |
| `${VAR}` | The environment variable `VAR`. If it isn't set, the load fails and names the variable. |
| `${VAR:-default}` | `default` when `VAR` is unset or empty. |
| `${file:/run/secrets/name}` | The file's contents without the trailing newline, for Docker and Kubernetes secrets. |
| `$${` | A literal `${`. |

Interpolated values count as secrets: they never appear in error messages or logs. Targets
(`to`) can't be interpolated.

Single braces, `{source}`, aren't interpolation. They're [templates](#templates), filled in for
each message.

## `server`

| Key | Description | Default |
| --- | --- | --- |
| `paths.webhooks` | The prefix every webhook is served under. Use `/` to serve webhooks at the root, for example to keep URLs from v1. | `/webhooks` |

## `brokers`

| Key | Description | Default |
| --- | --- | --- |
| `type` | `rabbitmq`, `kafka`, `sqs`, `sns`, `eventbridge`, `redis`, `google-pubsub`, `azure-servicebus`, `azure-eventhubs`, `http`, or `stdout` | required |
| `settings` | Connection settings for the type, documented in [brokers.md](brokers.md). Unknown keys are errors. | — |
| `publish.retry.max_retries` | Retries after the first attempt | `3` |
| `publish.retry.delay` | First retry delay | `2s` |
| `publish.retry.backoff` | `constant`, `linear`, or `exponential` | `exponential` |
| `publish.retry.factor` | What each exponential delay is multiplied by | `2` |
| `publish.retry.max_delay` | Cap on any single delay | `30s` |
| `publish.retry.jitter` | Spread each delay by ±25% | `true` |

Without a `publish.retry` block, a failed publish isn't retried.

## Targets

A target names a broker and, for brokers that have them, a topic. Write it one of three ways:

| Form | Example | Use |
| --- | --- | --- |
| `broker:topic` | `rabbit:github.push` | The usual case. It splits at the first `:`. |
| `broker` | `console` | Brokers without topics: `stdout`, and `http` posting to its base URL |
| An object | `{ broker: kafka, topic: payments }` | The same as `kafka:payments` |

What the topic means depends on the broker: a RabbitMQ routing key, a Kafka topic, a Redis
stream key, and so on ([brokers.md](brokers.md)). The loader checks that every target's broker
exists, that brokers which need a topic get one, and that SQS and SNS topics are in the broker's
map.

A topic can be a [template](#templates), such as `kafka:payments.{$.type}` or
`rabbit:{source}.events`. SQS and SNS map keys can't be checked at load when the topic is a
template; a rendered key that isn't in the map fails that publish.

`to` takes one target or a list. `to: none` (or `to: []`) means "acknowledge and publish
nothing". `none` can't be a broker ID.

## `webhooks`

| Key | Description | Default |
| --- | --- | --- |
| `path` | Route under `server.paths.webhooks`. May contain path parameters such as `{source}`. A leading `/` is allowed and means the same. | The webhook ID |
| `methods` | Allowed HTTP methods. Other methods get 405. | `[POST]` |
| `validate` | Validators that must all pass, in order. See [validators.md](validators.md). | none |
| `rate_limit` | `requests` per `window` (fixed window). Over the limit gets 429. | none |
| `message.body` | Include the request body | `true` |
| `message.headers` | `true` for every header, `false` for none, or a list of names | `false` |
| `message.metadata` | Values added to the envelope. Values are [templates](#templates), such as `"{source}"` or `"{header.X-Tenant:-none}"`. | none |
| `to` | Targets for every request. Use either `to` or `route`. | — |
| `route` | Ordered rules `{ when?, to }`. The first rule whose `when` matches picks the targets. The last rule must have no `when`. | — |

Routes must be unique, and none may shadow the service's own `/health`, `/health/*`, or
`/metrics`. Path parameters must be plain names; `webhook_id` and `topic` are reserved.

### Responses

| Status | When | Body |
| --- | --- | --- |
| 202 | At least one target succeeded, or there were no targets | `{"status":"accepted","webhook_id":…,"message_id":…}` |
| 401 | A validator failed | `{"error":"unauthorized"}` |
| 404 | The webhook was removed by a reload | — |
| 405 | The method isn't in `methods` | — |
| 429 | Over `rate_limit` | `{"error":"rate_limit_exceeded","webhook_id":…}` |
| 500 | Every target failed, or a metadata template didn't resolve | `{"error":"publish_failed","webhook_id":…}` |

## Templates

`{…}` fills in a value for each message, in target topics and `message.metadata` values:

| Placeholder | Value |
| --- | --- |
| `{name}` | The path parameter `name`, such as `{source}` |
| `{webhook_id}` | The webhook's ID |
| `{metadata.key}` | A `message.metadata` value. Topics only. |
| `{header.Name}` | A request header; the name is case-insensitive |
| `{$.json.path}` | One value from the JSON body, through a JSONPath such as `{$.customer.id}` |

```yaml
webhooks:
  ingest:
    path: ingest/{source}
    message:
      metadata:
        source: "{source}"
        tenant: "{header.X-Tenant:-shared}"
    to:
      - "kafka:payments.{$.type:-other}"     # payments.invoice.paid
      - "rabbit:{metadata.tenant}.{source}"  # acme.github
```

- **Defaults.** `{$.type:-other}` uses `other` when the placeholder has no value. It's the same
  `:-` as `${VAR:-default}`.
- **No value.** A placeholder has no value when the header is missing or empty, or the JSONPath
  selects nothing, `null`, an empty string, several values, or an object or array. Without a
  default, the message isn't published to that target. Other targets are unaffected, and the
  failure is logged and counted in `hookpipe_template_failures_total` as `template_unresolved`.
  A metadata value without a value fails every target, since they all get the same envelope.
- **Rendered topics** may only use letters, digits, `.`, `_`, and `-`. Anything else fails that
  target as `template_invalid`, which keeps request values from adding RabbitMQ wildcards, URL
  path segments, or odd Kafka topic names. A body-derived topic still lets the sender pick among
  topic names, so prefer [route rules](#routing-with-conditions) unless the value is signed and
  comes from a small known set.
- **Literal braces.** A `{` starts a placeholder only when a letter, `_`, or `$` follows it, and
  not right after a `$`, so `{}`, JSON text, and the `${NAME}` that `$${` leaves are kept as
  written. Write `{{` and `}}` for a literal `{` and `}` anywhere else.
- **Checked at load.** Every placeholder must be one of the forms above, name a path parameter
  the route has or a metadata key the webhook sets, and parse if it's a JSONPath. `{topic}` is
  reserved for ordering keys, which come with publish workers, so topics and metadata can't use it.

## Routing with conditions

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
| `jsonpath` | The JSON request body, through an [RFC 9535](https://www.rfc-editor.org/rfc/rfc9535) JSONPath. Paths must start with `$`. |
| `header` | A request header. The name is case-insensitive. |
| `path` | A path parameter of the webhook's route |

| Operator | Matches when |
| --- | --- |
| `eq: v` | The value equals `v`. A plain number or `true`/`false` compares by value (`100` matches `100.0`); anything else, including a quoted `"100"`, compares as text, where a JSON number's text is its literal. Header and path values have no type, so they match by text or by the same number or boolean. |
| `ne: v` | The value doesn't equal `v`. False when nothing is selected. |
| `in: [a, b]` | The value equals one of the list |
| `not_in: [a, b]` | The value equals none of the list. False when nothing is selected. |
| `gt`, `gte`, `lt`, `lte` | Numeric comparison. Needs a number; a JSON string or missing value doesn't match. |
| `contains: v`, `starts_with: v`, `ends_with: v` | Text comparison, ignoring case |
| `regex: p` | The .NET regex `p` matches (case-sensitive, 100 ms timeout) |
| `exists: true` / `exists: false` | At least one non-null value is selected / none is |

- **Several values.** When a JSONPath selects several values (`$.items[*].sku`), the condition
  holds if any of them matches. For "every item", negate the opposite:
  `not: { jsonpath: "$.items[*].qty", lte: 0 }`.
- **Filter selectors** cover what the operators don't:
  `{ jsonpath: "$.items[?@.qty > 10 && @.sku == 'A1']", exists: true }`.
- **Non-JSON bodies.** When the body isn't JSON, `jsonpath` conditions don't match, except
  `exists: false`.
- **Path parameters in values.** Text values of `eq`, `ne`, `in`, `not_in`, `contains`,
  `starts_with`, and `ends_with` can hold path-parameter placeholders:
  `{ header: X-Tenant, eq: "{tenant}" }`. Other placeholders and defaults aren't allowed there,
  and `regex` patterns are never templates, so `a{2,3}` keeps its meaning.
- **Checked at load.** Every JSONPath and regex is parsed when the config loads, and operator
  values must have the right type (`gt` needs a number, `in` needs a list).

`when` takes one condition, a list (all must match), or an `all`, `any`, or `not` node. Nodes
nest up to 8 levels deep.

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

## Environment variables

| Variable | Description | Default |
| --- | --- | --- |
| `HOOKPIPE_CONFIG_PATH` | Path to the YAML config file | `config/hookpipe.yaml` |
| `HOOKPIPE_MAX_BODY_SIZE_MB` | Max request body size in MB (service only); larger requests get 413 | Kestrel's default (~28.6 MB) |
| `Logging__LogLevel__Default` | Default log level | `Information` |
| `Logging__LogLevel__Runax` | Hookpipe log level (`Runax.*` categories) | `Information` |
| `Logging__LogLevel__Microsoft.AspNetCore` | ASP.NET log level | `Warning` |
| `SEQ_URL`, `SEQ_API_KEY` | Seq logging | disabled |
| `LOKI_URL` | Grafana Loki logging | disabled |

Any other variable is yours to name and reference from the config with `${…}`, for example
`${RABBITMQ_URL}`.

## Hot reload

Hookpipe watches the config file and reloads it 500 ms after the last change.

| Change | Takes effect |
| --- | --- |
| A webhook's `methods`, `validate`, `message`, `to`, `route` | On the next request |
| A broker's `publish.retry` | On the next publish |
| Adding or removing webhooks or brokers; changing a `path`, `server.paths`, a broker's `type` or `settings`, or a `rate_limit` | After a restart. The reload is applied, and the log lists what needs a restart, such as `brokers.audit (added)`. |

A reload is rejected as a whole, and the current config stays in force, when the file is invalid
or when a target uses a broker that isn't running (for example, a route to a broker added in the
same reload). The reason is logged.
