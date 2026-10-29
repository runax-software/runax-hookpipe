using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Builds a retry pipeline per broker retry policy. Pipelines are cached per <see cref="RetryConfiguration"/>
/// instance, so a configuration reload, which creates new instances, gets new pipelines with no extra work.
/// </summary>
internal sealed class RetryPipelines(ILoggerFactory loggerFactory)
{
    private readonly ConditionalWeakTable<RetryConfiguration, ResiliencePipeline> _pipelines = new();

    /// <summary>
    /// Returns the pipeline for a broker's retry policy, or null when it has none.
    /// </summary>
    public ResiliencePipeline? Get(string brokerId, RetryConfiguration? retry) =>
        retry is null || retry.MaxRetries == 0 ? null : _pipelines.GetValue(retry, policy => Build(brokerId, policy));

    /// <summary>
    /// The delay before retry <paramref name="attempt"/> (0 for the first retry), capped at
    /// <see cref="RetryConfiguration.MaxDelay"/>. With jitter, the delay is spread by ±25%.
    /// </summary>
    /// <param name="retry">The retry policy.</param>
    /// <param name="attempt">The retry number, starting at 0.</param>
    /// <param name="jitterSample">A random number in [0, 1), used only with jitter.</param>
    internal static TimeSpan DelayFor(RetryConfiguration retry, int attempt, double jitterSample)
    {
        var delay = retry.Backoff switch
        {
            RetryBackoff.Constant => retry.Delay,
            RetryBackoff.Linear => retry.Delay * (attempt + 1),
            _ => retry.Delay * Math.Pow(retry.Factor, attempt),
        };

        if (retry.Jitter) delay *= 0.75 + jitterSample / 2;
        return delay > retry.MaxDelay ? retry.MaxDelay : delay;
    }

    private ResiliencePipeline Build(string brokerId, RetryConfiguration retry)
    {
        var logger = loggerFactory.CreateLogger($"Runax.Hookpipe.Retry.{brokerId}");

        logger.LogDebug(
            "[Hookpipe.Broker:{BrokerId}] Retry policy: max={MaxRetries}, delay={Delay}, backoff={Backoff}, factor={Factor}",
            brokerId, retry.MaxRetries, retry.Delay, retry.Backoff, retry.Factor);

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = retry.MaxRetries,
                DelayGenerator = args =>
                    new ValueTask<TimeSpan?>(DelayFor(retry, args.AttemptNumber, Random.Shared.NextDouble())),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "[Hookpipe.Broker:{BrokerId}] Retry {Attempt}/{MaxRetries} after {Delay}ms: {Exception}",
                        brokerId, args.AttemptNumber + 1, retry.MaxRetries, args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }
}
