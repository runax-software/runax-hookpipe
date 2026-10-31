using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// The configuration source registered by <see cref="HookpipeConfiguratorExtensions.LoadYaml"/>.
/// </summary>
/// <remarks>
/// <see cref="Configuration"/> loads the file on first use and hands out the same result after that,
/// so a host that needs a setting before it builds (such as <c>server.max_body_size</c>) reads the
/// very configuration <see cref="HookpipeRuntime"/> runs on: one parse, one set of errors.
/// </remarks>
internal sealed class HookpipeOptions(string configurationPath, bool reloadOnChange)
{
    private HookpipeConfiguration? _configuration;

    /// <summary>
    /// Path to the YAML configuration file.
    /// </summary>
    public string ConfigurationPath { get; } = configurationPath;

    /// <summary>
    /// Whether the configuration file is watched and hot-reloaded.
    /// </summary>
    public bool ReloadOnChange { get; } = reloadOnChange;

    /// <summary>
    /// Whether the host took <c>server.max_body_size</c> to apply to its own server. Startup warns
    /// that the setting is ignored when it's configured and nobody took it, which is library mode.
    /// </summary>
    public bool ServerMaxBodySizeApplied { get; set; }

    /// <summary>
    /// The loaded configuration, read from the file on first use.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">The file isn't a valid v2 configuration.</exception>
    public HookpipeConfiguration Configuration => _configuration ??= ConfigurationLoader.Load(ConfigurationPath);
}
