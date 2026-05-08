# Logging

Hookpipe uses [Serilog](https://serilog.net/) for structured logging. Log destinations are configured via environment variables — no config files needed.

## Destinations

### Console

Always enabled. All log events are written to stdout.

### Seq

Enabled when `SEQ_URL` is set.

```bash
SEQ_URL=http://localhost:5341
SEQ_API_KEY=your-api-key  # optional
```

[Seq](https://datalust.co/seq) provides structured log search, dashboards, and alerting.

### Grafana Loki

Enabled when `LOKI_URL` is set.

```bash
LOKI_URL=http://localhost:3100
```

[Loki](https://grafana.com/oss/loki/) is Grafana's log aggregation system. Pair it with Grafana for dashboards and alerting.

### Multiple destinations

All destinations can be active simultaneously. If both `SEQ_URL` and `LOKI_URL` are set, logs are sent to Console + Seq + Loki in parallel.

## Log levels

Controlled via environment variables:

```bash
Serilog__MinimumLevel__Default=Information
Serilog__MinimumLevel__Override__Hookpipe=Debug
Serilog__MinimumLevel__Override__Microsoft.AspNetCore=Warning
```

| Level | Use |
|-------|-----|
| `Debug` | Detailed sink produce messages, internal state |
| `Information` | Startup, sink connections, request lifecycle |
| `Warning` | Recoverable issues |
| `Error` | Sink failures, unhandled exceptions |

## Environment variables

| Variable | Description | Default |
|----------|-------------|---------|
| `SEQ_URL` | Seq server URL | — (disabled) |
| `SEQ_API_KEY` | Seq API key | — (optional) |
| `LOKI_URL` | Grafana Loki URL | — (disabled) |
| `Serilog__MinimumLevel__Default` | Default log level | `Information` |
| `Serilog__MinimumLevel__Override__Hookpipe` | Hookpipe log level | inherits default |
| `Serilog__MinimumLevel__Override__Microsoft.AspNetCore` | ASP.NET log level | inherits default |
