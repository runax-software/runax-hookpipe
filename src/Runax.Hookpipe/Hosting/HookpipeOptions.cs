namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Settings captured from <see cref="HookpipeBuilder"/> when Hookpipe is registered.
/// </summary>
/// <param name="ConfigPath">Path to the YAML config file.</param>
/// <param name="ReloadOnChange">Whether the config file is watched and hot-reloaded.</param>
internal sealed record HookpipeOptions(string ConfigPath, bool ReloadOnChange);
