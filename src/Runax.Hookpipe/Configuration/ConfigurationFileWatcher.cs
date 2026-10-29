using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Watches the configuration file for changes and triggers a reload.
/// Debounces rapid changes to avoid reloading multiple times.
/// </summary>
public sealed class ConfigurationFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly HookpipeConfigurationProvider _configurationProvider;
    private readonly ILogger<ConfigurationFileWatcher> _logger;
    private Timer? _debounceTimer;

    /// <summary>
    /// Starts watching the configuration file. Changes are debounced by 500 ms before reloading.
    /// </summary>
    /// <param name="configurationPath">Path to the YAML configuration file to watch.</param>
    /// <param name="configurationProvider">The provider to reload when the file changes.</param>
    /// <param name="logger">Logger for this watcher.</param>
    public ConfigurationFileWatcher(string configurationPath, HookpipeConfigurationProvider configurationProvider, ILogger<ConfigurationFileWatcher> logger)
    {
        _configurationProvider = configurationProvider;
        _logger = logger;

        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath)) ?? ".";
        var fileName = Path.GetFileName(configurationPath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnChanged;
        _logger.LogInformation("[Hookpipe.Configuration] Watching '{Path}' for changes", configurationPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs @event)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            _logger.LogInformation("[Hookpipe.Configuration] File change detected, reloading");
            _configurationProvider.Reload();
        }, null, 500, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher.Dispose();
    }
}
