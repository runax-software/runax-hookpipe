# Metrics

Hookpipe exposes Prometheus metrics at `GET /metrics`.

## Available metrics

| Metric | Type | Labels | Description |
|--------|------|--------|-------------|
| `hookpipe_requests_total` | counter | endpoint_id, method, status_code | Total webhook requests received |
| `hookpipe_messages_produced_total` | counter | endpoint_id, sink_id | Messages successfully produced to sinks |
| `hookpipe_sink_errors_total` | counter | endpoint_id, sink_id | Sink errors (sink not found, produce failed) |
| `hookpipe_validation_failures_total` | counter | endpoint_id, validator_type | Validation failures (auth/signature rejected) |
| `hookpipe_request_duration_seconds` | histogram | endpoint_id | Request processing duration |

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
# Request rate per endpoint
rate(hookpipe_requests_total[5m])

# Error rate
rate(hookpipe_requests_total{status_code="500"}[5m])

# Validation failure rate
rate(hookpipe_validation_failures_total[5m])

# Messages produced per sink
rate(hookpipe_messages_produced_total[5m])

# P95 request duration
histogram_quantile(0.95, rate(hookpipe_request_duration_seconds_bucket[5m]))
```

## Built-in ASP.NET metrics

In addition to custom metrics, Hookpipe exposes standard ASP.NET Core HTTP metrics via `UseHttpMetrics()`:

- `http_request_duration_seconds`
- `http_requests_in_progress`
- `http_requests_received_total`
