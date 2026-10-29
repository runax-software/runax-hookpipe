using Runax.Hookpipe.Configuration.Sources;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Loads a Hookpipe configuration (format v2) from YAML. Loading is strict: unknown keys, missing values,
/// unknown references, and malformed conditions all fail the load, and the error lists every
/// problem with its configuration path.
/// </summary>
public static class ConfigurationLoader
{
    /// <summary>
    /// Loads the configuration from a YAML file.
    /// </summary>
    /// <param name="path">Absolute or relative path to the YAML file.</param>
    /// <returns>The loaded configuration.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">
    /// The file isn't a valid v2 configuration, including a v1 file without <c>version: 2</c>.
    /// </exception>
    public static HookpipeConfiguration Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Config file not found", path);

        return LoadFromString(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Loads the configuration from YAML text.
    /// </summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="source">Where the text came from, used in error messages.</param>
    /// <returns>The loaded configuration.</returns>
    /// <exception cref="HookpipeConfigurationException">The text isn't a valid v2 configuration.</exception>
    public static HookpipeConfiguration LoadFromString(string yaml, string source = "(string)")
    {
        var errors = new ConfigurationErrors();
        var root = YamlSource.Read(yaml, errors);
        var configuration = root is null && errors.HasErrors ? null : new HookpipeConfigurationBinder(errors).Bind(root);

        errors.ThrowIfAny(source);
        return configuration!;
    }
}
