using Hookpipe.Core.Config;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

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
                StdoutSink.TypeName => new StdoutSink(loggerFactory.CreateLogger<StdoutSink>()),
                RabbitMqSink.TypeName => await RabbitMqSink.CreateAsync(sinkConfig,
                    loggerFactory.CreateLogger<RabbitMqSink>()),
                KafkaSink.TypeName => KafkaSink.Create(sinkConfig, loggerFactory.CreateLogger<KafkaSink>()),
                HttpRelaySink.TypeName => HttpRelaySink.Create(sinkConfig, loggerFactory.CreateLogger<HttpRelaySink>()),
                SqsSink.TypeName => SqsSink.Create(sinkConfig, loggerFactory.CreateLogger<SqsSink>()),
                RedisStreamSink.TypeName => RedisStreamSink.Create(sinkConfig,
                    loggerFactory.CreateLogger<RedisStreamSink>()),
                GooglePubSubSink.TypeName => await GooglePubSubSink.CreateAsync(sinkConfig,
                    loggerFactory.CreateLogger<GooglePubSubSink>()),
                SnsSink.TypeName => SnsSink.Create(sinkConfig, loggerFactory.CreateLogger<SnsSink>()),
                EventBridgeSink.TypeName => EventBridgeSink.Create(sinkConfig,
                    loggerFactory.CreateLogger<EventBridgeSink>()),
                ServiceBusSink.TypeName => ServiceBusSink.Create(sinkConfig,
                    loggerFactory.CreateLogger<ServiceBusSink>()),
                EventHubSink.TypeName => EventHubSink.Create(sinkConfig, loggerFactory.CreateLogger<EventHubSink>()),
                _ => throw new InvalidOperationException($"Unknown sink type: '{sinkConfig.Type}'"),
            };

            logger.LogInformation("[Hookpipe.Sink:{Type}:{Id}] Initialized", sinkConfig.Type, sinkConfig.Id);
        }

        logger.LogInformation("[Hookpipe.Sink] Initialized {Count} sink(s)", sinks.Count);
        return sinks;
    }

    /// <summary>
    /// Creates retry pipelines for sinks that have a <see cref="RetryConfig"/> defined.
    /// Sinks without retry config are skipped.
    /// Uses exponential backoff with jitter via Polly.
    /// </summary>
    /// <param name="config">The loaded Hookpipe configuration.</param>
    /// <param name="loggerFactory">Logger factory for retry event logging.</param>
    /// <returns>Dictionary of <see cref="ResiliencePipeline"/> keyed by <see cref="SinkConfig.Id"/>.</returns>
    public static Dictionary<string, ResiliencePipeline> CreateRetryPipelines(
        HookpipeConfig config,
        ILoggerFactory loggerFactory)
    {
        var pipelines = new Dictionary<string, ResiliencePipeline>();

        foreach (var sinkConfig in config.Sinks)
        {
            if (sinkConfig.Retry is null) continue;
            var logger = loggerFactory.CreateLogger($"Hookpipe.Retry.{sinkConfig.Id}");

            pipelines[sinkConfig.Id] = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = sinkConfig.Retry.MaxRetries,
                    Delay = TimeSpan.FromSeconds(sinkConfig.Retry.DelaySeconds),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "[Hookpipe.Sink:{SinkId}] Retry {Attempt}/{MaxRetries} after {Delay}ms: {Exception}",
                            sinkConfig.Id, args.AttemptNumber + 1, sinkConfig.Retry.MaxRetries,
                            args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();

            logger.LogDebug(
                "[Hookpipe.Sink:{SinkId}] Retry policy: max={MaxRetries}, delay={Delay}s", sinkConfig.Id,
                sinkConfig.Retry.MaxRetries, sinkConfig.Retry.DelaySeconds);
        }

        return pipelines;
    }
}
