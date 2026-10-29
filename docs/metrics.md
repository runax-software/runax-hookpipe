# Metrics

Hookpipe exposes Prometheus metrics at `GET /metrics`.

## Available metrics

| Metric | Type | Labels | Description |
|--------|------|--------|-------------|
| `hookpipe_requests_total` | counter | webhook_id, method, status_code | Total webhook requests received |
| `hookpipe_messages_published_total` | counter | webhook_id, broker_id | Messages published to brokers |
| `hookpipe_publish_errors_total` | counter | webhook_id, broker_id | Failed publishes (after retries) |
| `hookpipe_validation_failures_total` | counter | webhook_id, validator_type | Validation failures (auth/signature rejected) |
| `hookpipe_request_duration_seconds` | histogram | webhook_id | Request processing duration |

Topics aren't a label: they'd make the label set unbounded.

## Scrape config

Add Hookpipe to your Prometheus config:

```yaml
scrape_configs:
  - job_name: hookpipe
    scrape_interval: 15s
    static_configs:
      - targets: ["hookpipe:8080"]
```

## Example queries

```promql
# Request rate per webhook
rate(hookpipe_requests_total[5m])

# Error rate
rate(hookpipe_requests_total{status_code="500"}[5m])

# Validation failure rate
rate(hookpipe_validation_failures_total[5m])

# Messages published per broker
sum by (broker_id) (rate(hookpipe_messages_published_total[5m]))

# P95 request duration
histogram_quantile(0.95, rate(hookpipe_request_duration_seconds_bucket[5m]))
```

## Built-in ASP.NET metrics

In addition to custom metrics, Hookpipe exposes standard ASP.NET Core HTTP metrics via `UseHttpMetrics()`:

- `http_request_duration_seconds`
- `http_requests_in_progress`
- `http_requests_received_total`
