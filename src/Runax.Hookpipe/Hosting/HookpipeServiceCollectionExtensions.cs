using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Registers Hookpipe in an ASP.NET Core application.
/// </summary>
public static class HookpipeServiceCollectionExtensions
{
    /// <summary>
    /// Registers Hookpipe's services. Map its routes with
    /// <see cref="HookpipeEndpointRouteBuilderExtensions.MapHookpipe"/> once the app is built.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures Hookpipe, for example <c>hookpipe =&gt; hookpipe.LoadYaml(path)</c>.</param>
    /// <returns>The same service collection, to allow chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Hookpipe is already registered or <paramref name="configure"/> sets no config source.
    /// </exception>
    public static IServiceCollection AddHookpipe(this IServiceCollection services, Action<HookpipeBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(HookpipeRuntime)))
            throw new InvalidOperationException("AddHookpipe may be called only once.");

        var builder = new HookpipeBuilder(services);
        configure(builder);

        var configPath = builder.ConfigPath
                         ?? throw new InvalidOperationException(
                             "Hookpipe has no config. Call hookpipe.LoadYaml(path) inside AddHookpipe.");
        var options = new HookpipeOptions(configPath, builder.ReloadOnChange);

        services.AddSingleton(options);
        services.AddSingleton(serviceProvider =>
            new HookpipeRuntime(options, serviceProvider.GetRequiredService<ILoggerFactory>()));
        services.AddHostedService<HookpipeHostedService>();

        return services;
    }
}
