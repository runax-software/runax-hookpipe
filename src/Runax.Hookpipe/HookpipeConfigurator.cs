using Microsoft.Extensions.DependencyInjection;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Hosting;

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

    /// <summary>
    /// The configuration source registered so far, or null when nothing is loaded yet.
    /// </summary>
    internal HookpipeOptions? Options =>
        Services.Select(descriptor => descriptor.ImplementationInstance).OfType<HookpipeOptions>().FirstOrDefault();

    /// <summary>
    /// Takes <c>server.max_body_size</c>, in bytes, for the host to apply to its own server, or null
    /// when the configuration doesn't set it. This loads the configuration, so call it after
    /// <see cref="HookpipeConfiguratorExtensions.LoadYaml"/>; Hookpipe then runs on the very
    /// configuration this returned, without reading the file twice.
    /// </summary>
    /// <remarks>
    /// Only a host that owns the whole process should call this, which is why the self-hosted service
    /// does and library hosts don't: the body limit belongs to the app's own Kestrel. Taking the
    /// setting also stops startup from warning that it was ignored. In library mode, use
    /// <c>webhooks.&lt;id&gt;.max_body_size</c>, which is applied per request
    /// (<see cref="WebhookConfiguration.MaxBodySize"/>).
    /// </remarks>
    /// <returns>The limit in bytes, or null to keep the server's own default.</returns>
    /// <exception cref="InvalidOperationException">No configuration is loaded yet.</exception>
    /// <exception cref="FileNotFoundException">The configuration file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">
    /// The configuration isn't a valid v2 configuration.
    /// </exception>
    public long? TakeServerMaxBodySize()
    {
        if (Options is not { } options)
        {
            throw new InvalidOperationException(
                "Hookpipe has no config yet. Load one (e.g. hookpipe.LoadYaml(\"config/hookpipe.yaml\")) " +
                "before taking 'server.max_body_size'.");
        }

        options.ServerMaxBodySizeApplied = true;
        return options.Configuration.Server.MaxBodySize;
    }
}
