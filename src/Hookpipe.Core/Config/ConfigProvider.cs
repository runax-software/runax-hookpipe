using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Config;

/// <summary>
/// Provides thread-safe access to the current Hookpipe configuration.
/// Supports hot-reload via <see cref="Reload"/>
/// </summary>
public sealed class ConfigProvider(string configPath, ILogger<ConfigProvider> logger)
{
    private volatile HookpipeConfig _config = ConfigLoader.Load(configPath);

    /// <summary>
    /// The current configuration. Thread-safe to read.
    /// </summary>
    public HookpipeConfig Current => _config;

    /// <summary>
    /// Reloads configuration from disk. Keeps the old config if reload fails.
    /// </summary>
    public void Reload()
    {
        try
        {
            var newConfig = ConfigLoader.Load(configPath);

            _config = newConfig;
            logger.LogInformation("[Hookpipe.Config] Reloaded {EndpointCount} endpoint(s) and {SinkCount} sink(s)",
                newConfig.Endpoints.Count, newConfig.Sinks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Hookpipe.Config] Failed to reload config, keeping current config");
        }
    }
}
