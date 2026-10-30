# Message Envelope

Every webhook request Hookpipe accepts is wrapped in a standardized envelope before it's published
to brokers.

## Format

```json
{
    "id": "13979c7e-108c-48b1-aac5-ff9220a3875b",
    "webhookId": "github-push",
    "receivedAt": "2026-05-07T16:23:55.644839+00:00",
    "method": "POST",
    "path": "/webhooks/github-push",
    "remoteAddress": "192.30.252.1",
    "headers": {
        "X-GitHub-Event": "push",
        "X-GitHub-Delivery": "72d3162e-cc78-11e3-81ab-4c9367dc0958"
    },
    "body": {
        "ref": "refs/heads/main",
        "repository": { "full_name": "owner/repo" }
    },
    "metadata": {
        "source": "github"
    }
}
```

## Fields

| Field           | Type               | Description                                                                                 |
| --------------- | ------------------ | ------------------------------------------------------------------------------------------- |
| `id`            | string             | Unique message ID (UUID), generated per request                                             |
| `webhookId`     | string             | ID of the webhook from config                                                               |
| `receivedAt`    | string             | ISO 8601 timestamp when the request was received                                            |
| `method`        | string             | HTTP method of the incoming request                                                         |
| `path`          | string             | Request path                                                                                |
| `remoteAddress` | string             | IP address of the caller                                                                    |
| `headers`       | object             | Request headers (filtered by config)                                                        |
| `body`          | object/string/null | Request body — parsed as JSON if valid, raw string otherwise, null if `body: false`         |
| `metadata`      | object             | Key-value pairs from `message.metadata`, with their templates rendered                     |

## What's included

The `message` config on each webhook controls what goes into the envelope:

- **`body: true`** (default) — body is included, parsed as JSON if possible
- **`body: false`** — body is null
- **`headers: true`** — all headers are included
- **`headers: [X-GitHub-Event, …]`** — only listed headers are included
- **`headers: false`** (default) — headers object is empty
- **`metadata`** — static values or [templates](configuration.md#templates) such as `{source}`, `{header.X-Tenant}`, or `{$.customer.id}`, rendered per message

## Headers and attributes

Brokers with message headers or attributes also carry the IDs there, so consumers can route
without parsing the body: `hookpipe.message.id` (the envelope `id`) and `hookpipe.webhook.id`.
Kafka records are keyed by the webhook ID. See [brokers.md](brokers.md) for each broker.
