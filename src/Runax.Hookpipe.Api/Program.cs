using Prometheus;
using Runax.Hookpipe;
using Runax.Hookpipe.Api;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console();

    var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
    if (!string.IsNullOrEmpty(seqUrl))
    {
        var seqApiKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");
        configuration.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
    }

    var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL");
    if (!string.IsNullOrEmpty(lokiUrl)) configuration.WriteTo.GrafanaLoki(lokiUrl);
});

var configurationPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH") ?? "config/hookpipe.yaml";

// The service owns the process, so it applies server.max_body_size to Kestrel. Taking it loads the
// config here, and Hookpipe then runs on that same result instead of reading the file again.
long? maxBodySize = null;
builder.Services.AddRunaxHookpipe(hookpipe =>
{
    hookpipe.LoadYaml(configurationPath);
    maxBodySize = hookpipe.TakeServerMaxBodySize();
});

if (maxBodySize is { } limit)
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = limit);

builder.Services.AddHostedService<HookpipeMetricsExporter>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapMetrics();

app.MapHookpipe();
app.MapGet("/health", () => Results.Ok());
app.MapHookpipeHealth();

app.Run();

/// <summary>
/// Entry point marker for ASP.NET Core integration tests.
/// </summary>
public partial class Program;
