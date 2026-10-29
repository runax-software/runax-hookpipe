using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Hosting;
using Runax.Hookpipe.Brokers.Health;
using Runax.Hookpipe.Webhooks;

namespace Runax.Hookpipe;

/// <summary>
/// Maps Hookpipe's routes in an ASP.NET Core application.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps one route per configured webhook, each at <c>{server.paths.webhooks}/{path}</c>
    /// (<c>/webhooks/{id}</c> by default). Requires
    /// <see cref="ServiceCollectionExtensions.AddRunaxHookpipe"/>.
    /// </summary>
    /// <remarks>
    /// Routes are registered once at startup, so paths can't change at runtime. Validation, routing,
    /// targets, and message configuration are read from the live configuration on each request.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <returns>A route group holding every webhook route, for adding conventions such as authorization.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Hookpipe isn't registered.</exception>
    public static RouteGroupBuilder MapHookpipe(this IEndpointRouteBuilder endpoints)
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<HookpipeRuntime>();
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger("Runax.Hookpipe.Startup");
        var configuration = runtime.ConfigurationProvider.Current;
        var prefix = configuration.Server.Paths.Webhooks;
        var group = endpoints.MapGroup(prefix == "/" ? string.Empty : prefix);

        foreach (var (id, webhook) in configuration.Webhooks)
        {
            var handler = new WebhookHandler(id, loggerFactory.CreateLogger($"Runax.Hookpipe.Webhook.{id}"), runtime);

            startupLogger.LogInformation("[Hookpipe.Webhook:{Id}] Registered {Methods} {Route}",
                id, string.Join("|", webhook.Methods), HookpipeRoutes.Combine(prefix, webhook.Path));

            group.Map($"/{webhook.Path}", handler.HandleAsync);
        }

        return group;
    }

    /// <summary>
    /// Maps the on-demand broker readiness endpoint, which probes every broker live on each request.
    /// Requires <see cref="ServiceCollectionExtensions.AddRunaxHookpipe"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder, usually the <c>WebApplication</c>.</param>
    /// <param name="pattern">The route pattern. Defaults to <c>/health/brokers</c>.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Hookpipe isn't registered.</exception>
    public static IEndpointConventionBuilder MapHookpipeHealth(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/health/brokers")
    {
        var runtime = endpoints.ServiceProvider.GetRequiredService<HookpipeRuntime>();
        return endpoints.MapBrokerHealthChecks(runtime.Brokers, pattern);
    }
}
