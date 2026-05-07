# Message Envelope

Every webhook received by Hookpipe is wrapped in a standardized envelope before being sent to a sink.

## Format

```json
{
    "id": "13979c7e-108c-48b1-aac5-ff9220a3875b",
    "endpointId": "github-push",
    "receivedAt": "2026-05-07T16:23:55.644839+00:00",
    "method": "POST",
    "path": "/github/push",
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
| `endpointId`    | string             | ID of the matched endpoint from config                                                      |
| `receivedAt`    | string             | ISO 8601 timestamp when the request was received                                            |
| `method`        | string             | HTTP method of the incoming request                                                         |
| `path`          | string             | Request path                                                                                |
| `remoteAddress` | string             | IP address of the caller                                                                    |
| `headers`       | object             | Request headers (filtered by config)                                                        |
| `body`          | object/string/null | Request body — parsed as JSON if valid, raw string otherwise, null if `include_body: false` |
| `metadata`      | object             | Static or path-param-derived key-value pairs from config                                    |

## What's included

The `message` config on each endpoint controls what goes into the envelope:

- **`include_body: true`** (default) — body is included, parsed as JSON if possible
- **`include_body: false`** — body is null
- **`include_headers: true`** — all headers are included
- **`include_headers: true` + `header_filter`** — only listed headers are included
- **`include_headers: false`** (default) — headers object is empty
- **`metadata`** — static values or `{param}` placeholders resolved from path parameters
