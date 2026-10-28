using Prometheus;
using Runax.Hookpipe.Hosting;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

if (int.TryParse(Environment.GetEnvironmentVariable("HOOKPIPE_MAX_BODY_SIZE_MB"), out var maxBodyMb))
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBodyMb * 1024 * 1024);

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration).WriteTo.Console();

    var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
    if (!string.IsNullOrEmpty(seqUrl))
    {
        var seqApiKey = Environment.GetEnvironmentVariable("SEQ_API_KEY");
        config.WriteTo.Seq(seqUrl, apiKey: seqApiKey);
    }

    var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL");
    if (!string.IsNullOrEmpty(lokiUrl)) config.WriteTo.GrafanaLoki(lokiUrl);
});

var configPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH") ?? "config/hookpipe.yaml";
builder.Services.AddHookpipe(hookpipe => hookpipe.LoadYaml(configPath));

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
