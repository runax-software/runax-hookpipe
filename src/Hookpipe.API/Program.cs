using System.Text.RegularExpressions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Validation;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var configPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH") ?? "config/hookpipe.yaml";
var config = ConfigLoader.Load(configPath);
var sinks = new Dictionary<string, ISink>();
var validators = new Dictionary<string, IValidator>
{
    ["bearer"] = new BearerTokenValidator(),
    ["hmac-sha256"] = new HmacSha256Validator(),
};

foreach (var sinkConfig in config.Sinks)
{
    sinks[sinkConfig.Id] = sinkConfig.Type switch
    {
        "stdout" => new StdoutSink(loggerFactory.CreateLogger<StdoutSink>()),
        "rabbitmq" => await RabbitMqSink.CreateAsync(sinkConfig, loggerFactory.CreateLogger<RabbitMqSink>()),
        "kafka" => KafkaSink.Create(sinkConfig, loggerFactory.CreateLogger<KafkaSink>()),
        _ => throw new InvalidOperationException($"Unknown sink type: '{sinkConfig.Type}'"),
    };
}

foreach (var endpoint in config.Endpoints)
{
    var paramNames = new List<string>();
    var pattern = "^" + Patterns.PathParamPattern().Replace(endpoint.Path, m =>
    {
        paramNames.Add(m.Groups[1].Value);
        return @"([^/]+)";
    }) + "$";
    var logger = loggerFactory.CreateLogger($"Hookpipe.Endpoint.{endpoint.Id}");

    app.Map(endpoint.Path, async context =>
    {
        if (!endpoint.Methods.Select(method => method.ToUpperInvariant()).ToHashSet().Contains(context.Request.Method))
        {
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
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
            }
        }

        try
        {
            Dictionary<string, string>? pathParams = null;
            var match = new Regex(pattern, RegexOptions.Compiled).Match(context.Request.Path.Value ?? "");
            if (match.Success && paramNames.Count > 0)
            {
                pathParams = [];
                for (var i = 0; i < paramNames.Count; i++)
                    pathParams[paramNames[i]] = match.Groups[i + 1].Value;
            }

            var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint, pathParams);
            await sinks[endpoint.Sink].ProduceAsync(envelope, context.RequestAborted);

            context.Response.StatusCode = 202;
            await context.Response.WriteAsJsonAsync(new { status = "accepted", endpoint_id = endpoint.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process request on endpoint '{EndpointId}'", endpoint.Id);
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
        if (sink is not IAsyncDisposable disposable) continue;

        shutdownLogger.LogInformation("Disposing sink '{SinkId}'", id);
        disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
});

app.Run();

internal static partial class Patterns
{
    [GeneratedRegex(@"\{(\w+)\}")]
    public static partial Regex PathParamPattern();
}
