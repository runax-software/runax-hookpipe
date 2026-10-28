using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Services;
using Runax.Hookpipe.Sinks.Health;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Maps Hookpipe's routes in an ASP.NET Core application.
/// </summary>
public static partial class HookpipeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps one route per configured endpoint. Requires
    /// <see cref="HookpipeServiceCollectionExtensions.AddHookpipe"/>.
    /// </summary>
    /// <remarks>
    /// Routes are registered once at startup, so paths can't change at runtime. Validation, sink
    /// routing, and message config are read from the live config on each request.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <returns>A route group holding every webhook route, for adding conventions such as authorization.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Hookpipe isn't registered.</exception>
    public static RouteGroupBuilder MapHookpipe(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<HookpipeRuntime>();
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger("Runax.Hookpipe.Startup");
        var group = endpoints.MapGroup(string.Empty);

        foreach (var endpoint in runtime.ConfigProvider.Current.Endpoints)
        {
            var paramNames = new List<string>();
            var pattern = "^" + PathParamPattern().Replace(Regex.Escape(endpoint.Path), match =>
            {
                paramNames.Add(match.Groups[1].Value);
                return @"([^/]+)";
            }) + "$";

            var handler = new WebhookHandler(
                endpointId: endpoint.Id,
                regex: new Regex(pattern, RegexOptions.Compiled),
                paramNames: paramNames,
                logger: loggerFactory.CreateLogger($"Runax.Hookpipe.Endpoint.{endpoint.Id}"),
                configProvider: runtime.ConfigProvider,
                sinks: runtime.Sinks,
                retryPipelines: runtime.RetryPipelines,
                validators: runtime.Validators,
                rateLimiters: runtime.RateLimiters,
                envelopeBuilder: runtime.EnvelopeBuilder);

            startupLogger.LogInformation("[Hookpipe.Endpoint:{Id}] Registered {Methods} {Path} -> sinks [{Sinks}]",
                endpoint.Id, string.Join("|", endpoint.Methods), endpoint.Path,
                string.Join(", ", endpoint.GetResolvedSinks()));

            group.Map(endpoint.Path, handler.HandleAsync);
        }

        return group;
    }

    /// <summary>
    /// Maps the on-demand sink readiness endpoint, which probes every sink live on each request.
    /// Requires <see cref="HookpipeServiceCollectionExtensions.AddHookpipe"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <param name="pattern">The route pattern. Defaults to <c>/health/sinks</c>.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Hookpipe isn't registered.</exception>
    public static IEndpointConventionBuilder MapHookpipeHealth(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/health/sinks")
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<HookpipeRuntime>();
        return endpoints.MapSinkHealthChecks(runtime.Sinks, pattern);
    }

    /// <summary>
    /// Matches path parameters in endpoint paths (e.g. "{source}" in "/ingest/{source}").
    /// </summary>
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PathParamPattern();
}
