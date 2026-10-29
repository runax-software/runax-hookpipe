using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Brokers;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Connects every configured broker and starts the configuration file watcher when the host starts, and disposes
/// them when it stops. Hosted services start before the server accepts requests, so brokers are
/// ready by the first request; a broker that fails to connect stops the host from starting.
/// </summary>
internal sealed class HookpipeHostedService(
    HookpipeOptions options,
    HookpipeRuntime runtime,
    ILoggerFactory loggerFactory) : IHostedService
{
    private ConfigurationFileWatcher? _watcher;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.ReloadOnChange)
            _watcher = new ConfigurationFileWatcher(
                options.ConfigurationPath, runtime.ConfigurationProvider, loggerFactory.CreateLogger<ConfigurationFileWatcher>());

        var brokers = await BrokerFactory.CreateAllAsync(runtime.ConfigurationProvider.Current, loggerFactory);
        foreach (var (id, broker) in brokers)
            runtime.Brokers[id] = broker;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var shutdownLogger = loggerFactory.CreateLogger("Runax.Hookpipe.Shutdown");

        if (_watcher is not null)
        {
            _watcher.Dispose();
            shutdownLogger.LogInformation("[Hookpipe.Shutdown] Stopped config file watcher");
        }

        foreach (var (id, rateLimiter) in runtime.RateLimiters)
        {
            rateLimiter.Dispose();
            shutdownLogger.LogDebug("[Hookpipe.Shutdown] Disposed rate limiter for '{WebhookId}'", id);
        }

        foreach (var (id, broker) in runtime.Brokers)
        {
            shutdownLogger.LogInformation("[Hookpipe.Shutdown] Disposing broker '{BrokerId}'", id);

            switch (broker)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
}
