using Hookpipe.Core.Config;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Creates sink instances from configuration.
/// </summary>
public static class SinkFactory
{
    /// <summary>
    /// Creates all sinks defined in the config and returns them keyed by sink ID.
    /// </summary>
    /// <param name="config">The loaded Hookpipe configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating sink-specific loggers.</param>
    /// <returns>Dictionary of sinks keyed by <see cref="SinkConfig.Id"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a sink type is unknown.</exception>
    public static async Task<Dictionary<string, ISink>> CreateAllAsync(
        HookpipeConfig config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(SinkFactory));
        var sinks = new Dictionary<string, ISink>();

        foreach (var sinkConfig in config.Sinks)
        {
            logger.LogDebug("[Hookpipe.Sink:{Type}:{Id}] Creating", sinkConfig.Type, sinkConfig.Id);

            sinks[sinkConfig.Id] = sinkConfig.Type switch
            {
                "stdout" => new StdoutSink(loggerFactory.CreateLogger<StdoutSink>()),
                "rabbitmq" => await RabbitMqSink.CreateAsync(sinkConfig, loggerFactory.CreateLogger<RabbitMqSink>()),
                "kafka" => KafkaSink.Create(sinkConfig, loggerFactory.CreateLogger<KafkaSink>()),
                _ => throw new InvalidOperationException($"Unknown sink type: '{sinkConfig.Type}'"),
            };

            logger.LogInformation("[Hookpipe.Sink:{Type}:{Id}] Initialized", sinkConfig.Type, sinkConfig.Id);
        }

        logger.LogInformation("[Hookpipe.Sink] Initialized {Count} sink(s)", sinks.Count);
        return sinks;
    }
}
