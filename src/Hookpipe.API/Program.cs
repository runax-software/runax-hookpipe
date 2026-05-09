using System.Text.RegularExpressions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Validation;
using Hookpipe.Core.Metrics;
using Prometheus;
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
var validators = ValidatorFactory.CreateAll(loggerFactory);
var envelopeBuilder = new EnvelopeBuilder(loggerFactory.CreateLogger<EnvelopeBuilder>());

// Routes are registered once at startup — paths can't change at runtime.
// Validation, sink routing, and message config are read from live config on each request.
foreach (var endpoint in configProvider.Current.Endpoints)
{
    var endpointId = endpoint.Id;
    var paramNames = new List<string>();
    var pattern = "^" + Patterns.PathParamPattern().Replace(Regex.Escape(endpoint.Path), method =>
    {
        paramNames.Add(method.Groups[1].Value);
        return @"([^/]+)";
    }) + "$";
    var regex = new Regex(pattern, RegexOptions.Compiled);
    var logger = loggerFactory.CreateLogger($"Hookpipe.Endpoint.{endpointId}");

    startupLogger.LogInformation("[Hookpipe.Endpoint:{Id}] Registered {Methods} {Path} -> sinks [{Sinks}]",
        endpointId, string.Join("|", endpoint.Methods), endpoint.Path, string.Join(", ", endpoint.GetResolvedSinks()));

    app.Map(endpoint.Path, async context =>
    {
        using var timer = HookpipeMetrics.RequestDuration.WithLabels(endpointId).NewTimer();

        // Look up live config for this endpoint
        var liveEndpoint = configProvider.Current.Endpoints.FirstOrDefault(e => e.Id == endpointId);
        if (liveEndpoint is null)
        {
            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Endpoint removed from config, returning 404",
                endpointId);
            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "404").Inc();
            context.Response.StatusCode = 404;
            return;
        }

        var methods = liveEndpoint.Methods.Select(m => m.ToUpperInvariant()).ToHashSet();

        if (!methods.Contains(context.Request.Method))
        {
            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Method not allowed: {Method}",
                endpointId, context.Request.Method);
            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "405").Inc();
            context.Response.StatusCode = 405;
            return;
        }

        if (liveEndpoint.Validation is not null)
        {
            IValidator? validator = null;

            if (liveEndpoint.Validation.Auth is not null)
                validators.TryGetValue(liveEndpoint.Validation.Auth.Type, out validator);
            else if (liveEndpoint.Validation.Signature is not null)
                validators.TryGetValue(liveEndpoint.Validation.Signature.Algorithm, out validator);

            if (validator is null || !await validator.ValidateAsync(context, liveEndpoint.Validation))
            {
                logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation failed, returning 401", endpointId);
                HookpipeMetrics.ValidationFailuresTotal.WithLabels(endpointId, validator?.Type ?? "unknown").Inc();
                HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "401").Inc();
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }

            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation passed ({ValidatorType})",
                endpointId, validator.Type);
        }

        try
        {
            Dictionary<string, string>? pathParams = null;
            var match = regex.Match(context.Request.Path.Value ?? "");

            if (match.Success && paramNames.Count > 0)
            {
                pathParams = [];
                for (var i = 0; i < paramNames.Count; i++)
                    pathParams[paramNames[i]] = match.Groups[i + 1].Value;

                logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Extracted {Count} path param(s)", endpointId,
                    pathParams.Count);
            }

            var envelope = await envelopeBuilder.BuildAsync(context, liveEndpoint, pathParams);
            var resolvedSinks = liveEndpoint.GetResolvedSinks();

            foreach (var sinkId in resolvedSinks)
            {
                if (!sinks.TryGetValue(sinkId, out var sink))
                {
                    logger.LogError("[Hookpipe.Endpoint:{EndpointId}] Sink '{SinkId}' not found", endpointId, sinkId);
                    HookpipeMetrics.SinkErrorsTotal.WithLabels(endpointId, sinkId).Inc();
                    continue;
                }

                await sink.ProduceAsync(envelope, context.RequestAborted);
                HookpipeMetrics.MessagesProducedTotal.WithLabels(endpointId, sinkId).Inc();
                logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Message '{MessageId}' produced to sink '{SinkId}'",
                    endpointId, envelope.Id, sinkId);
            }

            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "202").Inc();
            context.Response.StatusCode = 202;
            await context.Response.WriteAsJsonAsync(new { status = "accepted", endpoint_id = endpointId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Hookpipe.Endpoint:{EndpointId}] Failed to process request", endpointId);
            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "500").Inc();
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "internal_error", endpoint_id = endpointId });
        }
    });
}

app.MapGet("/health", () => Results.Ok());

app.Lifetime.ApplicationStopping.Register(() =>
{
    var shutdownLogger = loggerFactory.CreateLogger("Hookpipe.Shutdown");

    fileWatcher.Dispose();
    shutdownLogger.LogInformation("[Hookpipe.Shutdown] Stopped config file watcher");

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
