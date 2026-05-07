using System.Text.RegularExpressions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Hookpipe.Core.Sinks;
using Hookpipe.Sinks.Stdout;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["Hookpipe:ConfigPath"] ?? "config/hookpipe.yaml";
var config = ConfigLoader.Load(configPath);
var sinks = new Dictionary<string, ISink>();

foreach (var sinkConfig in config.Sinks)
{
    ISink sink = sinkConfig.Type switch
    {
        "stdout" => new StdoutSink(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<StdoutSink>()),
        _ => throw new InvalidOperationException($"Unknown sink type: '{sinkConfig.Type}'"),
    };

    sinks[sinkConfig.Id] = sink;
}

var app = builder.Build();

foreach (var endpoint in config.Endpoints)
{
    var paramNames = new List<string>();
    var pattern = "^" + Patterns.PathParamPattern().Replace(endpoint.Path, m =>
    {
        paramNames.Add(m.Groups[1].Value);
        return @"([^/]+)";
    }) + "$";
    var regex = new Regex(pattern, RegexOptions.Compiled);

    var sink = sinks[endpoint.Sink];
    var methods = endpoint.Methods.Select(m => m.ToUpperInvariant()).ToHashSet();

    app.Map(endpoint.Path, async context =>
    {
        if (!methods.Contains(context.Request.Method))
        {
            context.Response.StatusCode = 405;
            return;
        }

        Dictionary<string, string>? pathParams = null;
        var match = regex.Match(context.Request.Path.Value ?? "");
        if (match.Success && paramNames.Count > 0)
        {
            pathParams = new Dictionary<string, string>();
            for (var i = 0; i < paramNames.Count; i++)
                pathParams[paramNames[i]] = match.Groups[i + 1].Value;
        }

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint, pathParams);
        await sink.ProduceAsync(envelope, context.RequestAborted);

        context.Response.StatusCode = 200;
    });
}

app.MapGet("/health", () => Results.Ok());

app.Run();


internal static partial class Patterns
{
    [GeneratedRegex(@"\{(\w+)\}")]
    public static partial Regex PathParamPattern();
}