using System.Text.RegularExpressions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Validation;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

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
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var startupLogger = loggerFactory.CreateLogger("Hookpipe.Startup");

var configPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH") ?? "config/hookpipe.yaml";
var config = ConfigLoader.Load(configPath);
startupLogger.LogInformation("[Hookpipe.Config] Loaded {EndpointCount} endpoint(s) and {SinkCount} sink(s) from '{Path}'",
    config.Endpoints.Count, config.Sinks.Count, configPath);

var sinks = await SinkFactory.CreateAllAsync(config, loggerFactory);
var validators = ValidatorFactory.CreateAll(loggerFactory);

foreach (var endpoint in config.Endpoints)
{
    var paramNames = new List<string>();
    var pattern = "^" + Patterns.PathParamPattern().Replace(Regex.Escape(endpoint.Path), method =>
    {
        paramNames.Add(method.Groups[1].Value);
        return @"([^/]+)";
    }) + "$";
    var regex = new Regex(pattern, RegexOptions.Compiled);
    var methods = endpoint.Methods.Select(method => method.ToUpperInvariant()).ToHashSet();
    var logger = loggerFactory.CreateLogger($"Hookpipe.Endpoint.{endpoint.Id}");
    var envelopeBuilder = new EnvelopeBuilder(loggerFactory.CreateLogger<EnvelopeBuilder>());

    startupLogger.LogInformation("[Hookpipe.Endpoint:{Id}] Registered {Methods} {Path} -> sink '{Sink}'",
        endpoint.Id, string.Join("|", methods), endpoint.Path, endpoint.Sink);

    app.Map(endpoint.Path, async context =>
    {
        if (!methods.Contains(context.Request.Method))
        {
            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Method not allowed: {Method}",
                endpoint.Id, context.Request.Method);
            context.Response.StatusCode = 405;
            return;
        }

        if (endpoint.Validation is not null)
        {
            IValidator? validator = null;

            if (endpoint.Validation.Auth is not null)
                validators.TryGetValue(endpoint.Validation.Auth.Type, out validator);
            else if (endpoint.Validation.Signature is not null)
                validators.TryGetValue(endpoint.Validation.Signature.Algorithm, out validator);

            if (validator is null || !await validator.ValidateAsync(context, endpoint.Validation))
            {
                logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation failed, returning 401",
                    endpoint.Id);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }

            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation passed ({ValidatorType})",
                endpoint.Id, validator.Type);
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

                logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Extracted {Count} path param(s)",
                    endpoint.Id, pathParams.Count);
            }

            var envelope = await envelopeBuilder.BuildAsync(context, endpoint, pathParams);
            await sinks[endpoint.Sink].ProduceAsync(envelope, context.RequestAborted);

            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Message '{MessageId}' produced to sink '{SinkId}'",
                endpoint.Id, envelope.Id, endpoint.Sink);

            context.Response.StatusCode = 202;
            await context.Response.WriteAsJsonAsync(new { status = "accepted", endpoint_id = endpoint.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Hookpipe.Endpoint:{EndpointId}] Failed to process request", endpoint.Id);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "internal_error", endpoint_id = endpoint.Id });
        }
    });
}

app.MapGet("/health", () => Results.Ok());

app.Lifetime.ApplicationStopping.Register(() =>
{
    var shutdownLogger = loggerFactory.CreateLogger("Hookpipe.Shutdown");
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
