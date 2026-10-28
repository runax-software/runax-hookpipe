using Microsoft.Extensions.DependencyInjection;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Configures Hookpipe inside
/// <see cref="HookpipeServiceCollectionExtensions.AddHookpipe(IServiceCollection, Action{HookpipeBuilder})"/>.
/// </summary>
public sealed class HookpipeBuilder
{
    internal HookpipeBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    /// The service collection Hookpipe is being registered in.
    /// </summary>
    public IServiceCollection Services { get; }

    internal string? ConfigPath { get; private set; }

    internal bool ReloadOnChange { get; private set; }

    /// <summary>
    /// Loads endpoints and sinks from a YAML config file.
    /// </summary>
    /// <param name="path">Absolute or relative path to the YAML config file.</param>
    /// <param name="reloadOnChange">Whether to watch the file and hot-reload it when it changes.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    public HookpipeBuilder LoadYaml(string path, bool reloadOnChange = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ConfigPath = path;
        ReloadOnChange = reloadOnChange;
        return this;
    }
}
