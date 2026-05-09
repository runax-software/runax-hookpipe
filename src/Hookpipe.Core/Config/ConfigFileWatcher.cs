using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Config;

/// <summary>
/// Watches the config file for changes and triggers a reload.
/// Debounces rapid changes to avoid reloading multiple times.
/// </summary>
public sealed class ConfigFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConfigProvider _configProvider;
    private readonly ILogger<ConfigFileWatcher> _logger;
    private Timer? _debounceTimer;

    public ConfigFileWatcher(string configPath, ConfigProvider configProvider, ILogger<ConfigFileWatcher> logger)
    {
        _configProvider = configProvider;
        _logger = logger;

        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        var fileName = Path.GetFileName(configPath);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnChanged;
        _logger.LogInformation("[Hookpipe.Config] Watching '{Path}' for changes", configPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs @event)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            _logger.LogInformation("[Hookpipe.Config] File change detected, reloading");
            _configProvider.Reload();
        }, null, 500, Timeout.Infinite);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher.Dispose();
    }
}
