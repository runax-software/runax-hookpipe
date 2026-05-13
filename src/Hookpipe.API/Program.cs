using System.Text.RegularExpressions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Validation;
using Prometheus;
using Serilog;
using Serilog.Sinks.Grafana.Loki;
using System.Threading.RateLimiting;

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

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapMetrics();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var startupLogger = loggerFactory.CreateLogger("Hookpipe.Startup");
var configPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH") ?? "config/hookpipe.yaml";

var configProvider = new ConfigProvider(configPath, loggerFactory.CreateLogger<ConfigProvider>());
var fileWatcher = new ConfigFileWatcher(configPath, configProvider, loggerFactory.CreateLogger<ConfigFileWatcher>());

startupLogger.LogInformation(
    "[Hookpipe.Config] Loaded {EndpointCount} endpoint(s) and {SinkCount} sink(s) from '{Path}'",
    configProvider.Current.Endpoints.Count, configProvider.Current.Sinks.Count, configPath);

var sinks = await SinkFactory.CreateAllAsync(configProvider.Current, loggerFactory);
var retryPipelines = SinkFactory.CreateRetryPipelines(configProvider.Current, loggerFactory);
var validators = ValidatorFactory.CreateAll(loggerFactory);
var envelopeBuilder = new EnvelopeBuilder(loggerFactory.CreateLogger<EnvelopeBuilder>());

// Build rate limiters for endpoints that have rate_limit configured
var rateLimiters = new Dictionary<string, RateLimiter>();
foreach (var ep in configProvider.Current.Endpoints)
{
    if (ep.RateLimit is null) continue;

    rateLimiters[ep.Id] = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
    {
        PermitLimit = ep.RateLimit.Requests,
        Window = TimeSpan.FromSeconds(ep.RateLimit.WindowSeconds),
        QueueLimit = 0,
    });

    startupLogger.LogInformation(
        "[Hookpipe.Endpoint:{Id}] Rate limit: {Requests} requests per {Window}s",
        ep.Id, ep.RateLimit.Requests, ep.RateLimit.WindowSeconds);
}

// Routes are registered once at startup — paths can't change at runtime.
// Validation, sink routing, and message config are read from live config on each request.
foreach (var endpoint in configProvider.Current.Endpoints)
{
    var paramNames = new List<string>();
    var pattern = "^" + Patterns.PathParamPattern().Replace(Regex.Escape(endpoint.Path), method =>
    {
        paramNames.Add(method.Groups[1].Value);
        return @"([^/]+)";
    }) + "$";
    var regex = new Regex(pattern, RegexOptions.Compiled);

    var handler = new WebhookHandler(
        endpointId: endpoint.Id,
        regex: regex,
        paramNames: paramNames,
        logger: loggerFactory.CreateLogger($"Hookpipe.Endpoint.{endpoint.Id}"),
        configProvider: configProvider,
        sinks: sinks,
        retryPipelines: retryPipelines,
        validators: validators,
        rateLimiters: rateLimiters,
        envelopeBuilder: envelopeBuilder);

    startupLogger.LogInformation("[Hookpipe.Endpoint:{Id}] Registered {Methods} {Path} -> sinks [{Sinks}]",
        endpoint.Id, string.Join("|", endpoint.Methods), endpoint.Path, string.Join(", ", endpoint.GetResolvedSinks()));

    app.Map(endpoint.Path, handler.HandleAsync);
}

app.MapGet("/health", () => Results.Ok());

app.Lifetime.ApplicationStopping.Register(() =>
{
    var shutdownLogger = loggerFactory.CreateLogger("Hookpipe.Shutdown");

    fileWatcher.Dispose();
    shutdownLogger.LogInformation("[Hookpipe.Shutdown] Stopped config file watcher");

    foreach (var (id, rl) in rateLimiters)
    {
        rl.Dispose();
        shutdownLogger.LogDebug("[Hookpipe.Shutdown] Disposed rate limiter for '{EndpointId}'", id);
    }

    foreach (var (id, sink) in sinks)
    {
        shutdownLogger.LogInformation("[Hookpipe.Shutdown] Disposing sink '{SinkId}'", id);

        switch (sink)
        {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
});

app.Run();

/// <summary>
/// Generated regex patterns used for path parameter extraction.
/// </summary>
internal static partial class Patterns
{
    /// <summary>
    /// Matches path parameters in endpoint paths (e.g. "{source}" in "/ingest/{source}").
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> matching "{paramName}" patterns.</returns>
    [GeneratedRegex(@"\{(\w+)\}")]
    public static partial Regex PathParamPattern();
}

/// <summary>
/// Entry point marker for ASP.NET Core integration tests.
/// </summary>
public partial class Program;
