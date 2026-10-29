using Microsoft.Extensions.DependencyInjection;
using Runax.Hookpipe.Hosting;

namespace Runax.Hookpipe;

/// <summary>
/// Configurator extensions for loading Hookpipe's configuration.
/// </summary>
public static class HookpipeConfiguratorExtensions
{
    /// <summary>
    /// Loads brokers and webhooks from a YAML configuration file (format v2).
    /// </summary>
    /// <param name="configurator">The Hookpipe configurator.</param>
    /// <param name="path">Absolute or relative path to the YAML configuration file.</param>
    /// <param name="reloadOnChange">Whether to watch the file and hot-reload it when it changes.</param>
    /// <returns>The same configurator, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">A YAML file is already loaded.</exception>
    public static HookpipeConfigurator LoadYaml(
        this HookpipeConfigurator configurator,
        string path,
        bool reloadOnChange = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var services = configurator.Services;

        var loaded = services
            .Select(d => d.ImplementationInstance)
            .OfType<HookpipeOptions>()
            .FirstOrDefault();
        if (loaded is not null)
        {
            throw new InvalidOperationException(
                $"Hookpipe already loads '{loaded.ConfigurationPath}'. Call LoadYaml only once.");
        }

        services.AddSingleton(new HookpipeOptions(path, reloadOnChange));

        return configurator;
    }
}
