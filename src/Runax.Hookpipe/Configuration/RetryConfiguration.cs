namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Retry policy for a broker's publishes, the <c>brokers.&lt;id&gt;.publish.retry</c> section.
/// </summary>
public sealed class RetryConfiguration
{
    /// <summary>
    /// Retries after the first attempt. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// The first retry's delay. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How the delay grows between retries. Defaults to <see cref="RetryBackoff.Exponential"/>.
    /// </summary>
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;

    /// <summary>
    /// What each delay is multiplied by with <see cref="RetryBackoff.Exponential"/>. Defaults to 2.
    /// </summary>
    public double Factor { get; init; } = 2;

    /// <summary>
    /// The cap on any single delay. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether delays are randomized, so retries from many requests don't line up. Defaults to true.
    /// </summary>
    public bool Jitter { get; init; } = true;
}
