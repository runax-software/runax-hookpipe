# Runax.Hookpipe

Config-driven webhook gateway for ASP.NET Core. Hookpipe receives webhooks on routes defined in a
YAML file, validates them (HMAC, Stripe, bearer, API key, IP allowlist), wraps them in a standard
message envelope, and routes them to message brokers.

This package is the whole engine. The same engine also runs as a self-hosted service, the
`ghcr.io/runax-software/runax-hookpipe` image.

## Use it in your app

```csharp
using Runax.Hookpipe.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHookpipe(hookpipe => hookpipe.LoadYaml("config/hookpipe.yaml"));

var app = builder.Build();
app.MapHookpipe();          // one route per configured endpoint
app.MapHookpipeHealth();    // /health/sinks: live probe of every sink
app.Run();
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

Configuration, validators, sinks, and the message envelope are documented in the
[repository](https://github.com/runax-software/runax-hookpipe).
