using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Config;
using Runax.Hookpipe.Sinks;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Connects sinks and starts the config file watcher when the host starts, and disposes them when
/// it stops. Hosted services start before the server accepts requests, so sinks are ready by the
/// first request; a sink that fails to connect stops the host from starting.
/// </summary>
internal sealed class HookpipeHostedService(
    HookpipeOptions options,
    HookpipeRuntime runtime,
    ILoggerFactory loggerFactory) : IHostedService
{
    private ConfigFileWatcher? _watcher;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.ReloadOnChange)
        {
            _watcher = new ConfigFileWatcher(
                options.ConfigPath, runtime.ConfigProvider, loggerFactory.CreateLogger<ConfigFileWatcher>());
        }

        var sinks = await SinkFactory.CreateAllAsync(runtime.ConfigProvider.Current, loggerFactory);
        foreach (var (id, sink) in sinks)
            runtime.Sinks[id] = sink;
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
            shutdownLogger.LogDebug("[Hookpipe.Shutdown] Disposed rate limiter for '{EndpointId}'", id);
        }

        foreach (var (id, sink) in runtime.Sinks)
        {
            shutdownLogger.LogInformation("[Hookpipe.Shutdown] Disposing sink '{SinkId}'", id);

            switch (sink)
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
