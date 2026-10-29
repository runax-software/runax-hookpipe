using Prometheus;
using Runax.Hookpipe;
using Runax.Hookpipe.Api;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

if (int.TryParse(Environment.GetEnvironmentVariable("HOOKPIPE_MAX_BODY_SIZE_MB"), out var maxBodyMb))
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBodyMb * 1024 * 1024);

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
builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(configurationPath));
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
