namespace Runax.Hookpipe.Hosting;

/// <summary>
/// The configuration source registered by <see cref="HookpipeConfiguratorExtensions.LoadYaml"/>.
/// </summary>
/// <param name="ConfigurationPath">Path to the YAML configuration file.</param>
/// <param name="ReloadOnChange">Whether the configuration file is watched and hot-reloaded.</param>
internal sealed record HookpipeOptions(string ConfigurationPath, bool ReloadOnChange);
