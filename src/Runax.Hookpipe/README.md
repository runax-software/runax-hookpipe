# Runax.Hookpipe

Config-driven webhook gateway for ASP.NET Core. Hookpipe receives webhooks on routes defined in a
YAML file, validates them (HMAC, Stripe, bearer, API key, IP allowlist), routes them by header or
JSON body (JSONPath), wraps them in a standard message envelope, and publishes them to message
brokers.

This package is the whole engine. The same engine also runs as a self-hosted service, the
`ghcr.io/runax-software/runax-hookpipe` image.

## Use it in your app

```csharp
using Runax.Hookpipe;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml("config/hookpipe.yaml"));

var app = builder.Build();
app.MapHookpipe();          // one route per webhook, under /webhooks by default
app.MapHookpipeHealth();    // /health/brokers: live probe of every broker
app.Run();
```

A minimal `hookpipe.yaml`:

```yaml
version: 2
brokers:
  rabbit:
    type: rabbitmq
    settings:
      uri: ${RABBITMQ_URL}
webhooks:
  github:                     # POST /webhooks/github
    validate:
      - type: hmac-sha256
        header: X-Hub-Signature-256
        secret: ${GITHUB_WEBHOOK_SECRET}
    to: rabbit:github.events
```

`MapHookpipe()` returns a `RouteGroupBuilder`, so ASP.NET Core conventions such as
`.RequireAuthorization()` or `.RequireRateLimiting()` apply to every webhook route. The YAML file
is watched and hot-reloaded; pass `reloadOnChange: false` to turn that off.

## Metrics

Hookpipe records on the `Runax.Hookpipe` meter (`HookpipeDiagnostics.MeterName`) and doesn't
pick an exporter. With OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(HookpipeDiagnostics.MeterName));
```

## Documentation

Configuration, validators, brokers, and the message envelope are documented in the
[repository](https://github.com/runax-software/runax-hookpipe).
