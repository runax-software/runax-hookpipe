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
    /// Creates a configurator over the given service collection. Internal:
    /// <see cref="ServiceCollectionExtensions.AddRunaxHookpipe"/> builds the one instance, and a
    /// configurator is meaningless outside its callback.
    /// </summary>
    /// <param name="services">The service collection that Hookpipe registers into.</param>
    internal HookpipeConfigurator(IServiceCollection services) => Services = services;

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
    /// Applies <c>server.max_body_size</c> to the host's own server. <paramref name="apply"/> runs
    /// only when the configuration sets the key, and receives the limit in bytes. This loads the
    /// configuration, so call it after <see cref="HookpipeConfiguratorExtensions.LoadYaml"/>;
    /// Hookpipe then runs on that same configuration, without reading the file twice.
    /// </summary>
    /// <remarks>
    /// Only a host that owns the whole process should call this, which is why the self-hosted service
    /// does and library hosts don't: the body limit belongs to the app's own Kestrel. Calling it is
    /// what claims the setting, so startup no longer warns that it was ignored — whether or not the
    /// key turned out to be set. In library mode, use <c>webhooks.&lt;id&gt;.max_body_size</c>, which
    /// is applied per request (<see cref="WebhookConfiguration.MaxBodySize"/>) and so works no matter
    /// who owns the server.
    /// </remarks>
    /// <param name="apply">
    /// Receives the limit in bytes, for example
    /// <c>limit =&gt; builder.WebHost.ConfigureKestrel(o =&gt; o.Limits.MaxRequestBodySize = limit)</c>.
    /// </param>
    /// <returns>The same configurator, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="apply"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No configuration is loaded yet.</exception>
    /// <exception cref="FileNotFoundException">The configuration file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">
    /// The configuration isn't a valid v2 configuration.
    /// </exception>
    public HookpipeConfigurator UseServerMaxBodySize(Action<long> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (Options is not { } options)
        {
            throw new InvalidOperationException(
                "Hookpipe has no config yet. Load one (e.g. hookpipe.LoadYaml(\"config/hookpipe.yaml\")) " +
                "before applying 'server.max_body_size'.");
        }

        options.ServerMaxBodySizeApplied = true;

        if (options.Configuration.Server.MaxBodySize is { } limit) apply(limit);

        return this;
    }
}
