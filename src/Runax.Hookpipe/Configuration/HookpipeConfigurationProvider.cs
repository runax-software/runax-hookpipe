using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Provides thread-safe access to the current Hookpipe configuration and reloads it on demand.
/// </summary>
/// <remarks>
/// Brokers, routes, and rate limiters are created once at startup, so a reload applies only the
/// changes that can take effect live (validators, messages, targets, routes, retry policies) and
/// logs the rest as needing a restart. A reload whose targets use a broker that isn't running is
/// rejected as a whole, and the current configuration stays in force.
/// </remarks>
public sealed class HookpipeConfigurationProvider
{
    private readonly string _configurationPath;
    private readonly ILogger<HookpipeConfigurationProvider> _logger;
    private readonly HookpipeConfiguration _running;
    private volatile HookpipeConfiguration _configuration;

    /// <summary>
    /// Loads the configuration file.
    /// </summary>
    /// <param name="configurationPath">Path to the YAML configuration file.</param>
    /// <param name="logger">Logger for reloads.</param>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="HookpipeConfigurationException">The file isn't a valid v2 configuration.</exception>
    public HookpipeConfigurationProvider(string configurationPath, ILogger<HookpipeConfigurationProvider> logger)
    {
        _configurationPath = configurationPath;
        _logger = logger;
        _running = _configuration = ConfigurationLoader.Load(configurationPath);
    }

    /// <summary>
    /// The current configuration. Thread-safe to read.
    /// </summary>
    public HookpipeConfiguration Current => _configuration;

    /// <summary>
    /// Reloads the configuration file. Keeps the current configuration if the file is invalid or the reload is rejected.
    /// </summary>
    /// <returns>True when the new configuration was applied.</returns>
    public bool Reload()
    {
        try
        {
            var next = ConfigurationLoader.Load(_configurationPath);
            var check = ConfigurationReloadCheck.Compare(_running, next);

            if (check.Rejections.Count > 0)
            {
                _logger.LogError("[Hookpipe.Configuration] Reload rejected, keeping the current config: {Reasons}",
                    string.Join("; ", check.Rejections));
                return false;
            }

            if (check.RestartRequired.Count > 0)
            {
                _logger.LogWarning("[Hookpipe.Configuration] Restart required for: {Changes}",
                    string.Join(", ", check.RestartRequired));
            }

            _configuration = next;
            _logger.LogInformation("[Hookpipe.Configuration] Reloaded {WebhookCount} webhook(s) and {BrokerCount} broker(s)",
                next.Webhooks.Count, next.Brokers.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hookpipe.Configuration] Failed to reload config, keeping the current config");
            return false;
        }
    }
}
