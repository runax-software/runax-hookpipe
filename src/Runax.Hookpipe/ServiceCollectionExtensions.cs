using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runax.Hookpipe.Hosting;

namespace Runax.Hookpipe;

/// <summary>
/// Extension methods for registering Runax.Hookpipe services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Runax Hookpipe module and applies the given configuration. Load a configuration
    /// (e.g. <c>hookpipe.LoadYaml("config/hookpipe.yaml")</c>), then map the webhooks with
    /// <see cref="EndpointRouteBuilderExtensions.MapHookpipe"/> once the app is built.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action that loads Hookpipe's configuration.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Hookpipe is already registered, or <paramref name="configure"/> loaded no configuration.
    /// </exception>
    public static IServiceCollection AddRunaxHookpipe(
        this IServiceCollection services,
        Action<HookpipeConfigurator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(d => d.ServiceType == typeof(HookpipeRuntime)))
            throw new InvalidOperationException("AddRunaxHookpipe may be called only once.");

        configure(new HookpipeConfigurator(services));

        if (!services.Any(d => d.ImplementationInstance is HookpipeOptions))
        {
            throw new InvalidOperationException(
                "Hookpipe has no config. Load one inside AddRunaxHookpipe " +
                "(e.g. hookpipe.LoadYaml(\"config/hookpipe.yaml\")).");
        }

        // Loads the configuration and holds what the mapped webhooks share; the hosted service connects
        // the brokers and watches the file.
        services.TryAddSingleton<HookpipeRuntime>();
        services.AddHostedService<HookpipeHostedService>();

        return services;
    }
}
