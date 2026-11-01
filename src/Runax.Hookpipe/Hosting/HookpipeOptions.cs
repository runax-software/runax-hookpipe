using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Configuration.Sources;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Where Hookpipe's definitions come from: a YAML file
/// (<see cref="HookpipeConfiguratorExtensions.LoadYaml"/>) or definitions built in code.
/// </summary>
/// <remarks>
/// <see cref="Configuration"/> loads on first use and hands out the same result after that, so a
/// host that needs a setting before it builds (such as <c>server.max_body_size</c>) reads the very
/// configuration <see cref="HookpipeRuntime"/> runs on: one load, one set of errors.
/// </remarks>
internal sealed class HookpipeOptions
{
    private readonly Func<HookpipeConfiguration> _load;
    private HookpipeConfiguration? _configuration;

    private HookpipeOptions(Func<HookpipeConfiguration> load, string? configurationPath, bool reloadOnChange)
    {
        _load = load;
        ConfigurationPath = configurationPath;
        ReloadOnChange = reloadOnChange;
    }

    /// <summary>
    /// Reads the definitions from a YAML file, optionally watching it for changes.
    /// </summary>
    public static HookpipeOptions FromYaml(string path, bool reloadOnChange) =>
        new(() => ConfigurationLoader.Load(path), path, reloadOnChange);

    /// <summary>
    /// Takes the definitions built in code. There's no file to watch, so these never reload:
    /// they're fixed for the life of the process.
    /// </summary>
    public static HookpipeOptions FromCode(CodeSource source) =>
        new(source.Build, configurationPath: null, reloadOnChange: false);

    /// <summary>
    /// Path to the YAML configuration file, or null when the definitions come from code.
    /// </summary>
    public string? ConfigurationPath { get; }

    /// <summary>
    /// Whether the configuration file is watched and hot-reloaded. Always false for code.
    /// </summary>
    public bool ReloadOnChange { get; }

    /// <summary>
    /// Whether the host took <c>server.max_body_size</c> to apply to its own server. Startup warns
    /// that the setting is ignored when it's configured and nobody took it, which is library mode.
    /// </summary>
    public bool ServerMaxBodySizeApplied { get; set; }

    /// <summary>
    /// Where the definitions came from, for log and error messages.
    /// </summary>
    public string Source => ConfigurationPath ?? "code";

    /// <summary>
    /// The loaded configuration, produced on first use.
    /// </summary>
    /// <exception cref="FileNotFoundException">The configuration file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">The definitions aren't valid.</exception>
    public HookpipeConfiguration Configuration => _configuration ??= _load();
}
