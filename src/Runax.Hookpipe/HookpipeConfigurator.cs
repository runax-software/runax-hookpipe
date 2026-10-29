using Microsoft.Extensions.DependencyInjection;

namespace Runax.Hookpipe;

/// <summary>
/// Fluent surface for configuring Hookpipe inside
/// <see cref="ServiceCollectionExtensions.AddRunaxHookpipe"/>. The configuration is loaded through the
/// <see cref="HookpipeConfiguratorExtensions.LoadYaml"/> extension method.
/// </summary>
public sealed class HookpipeConfigurator
{
    /// <summary>
    /// Creates a configurator over the given service collection.
    /// </summary>
    /// <param name="services">The service collection that Hookpipe registers into.</param>
    public HookpipeConfigurator(IServiceCollection services) => Services = services;

    /// <summary>
    /// Gets the underlying service collection that Hookpipe registers into.
    /// </summary>
    public IServiceCollection Services { get; }
}
