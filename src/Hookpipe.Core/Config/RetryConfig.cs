namespace Hookpipe.Core.Config;

/// <summary>
/// Retry policy configuration for a sink.
/// Uses exponential backoff with optional jitter.
/// </summary>
public sealed class RetryConfig
{
    /// <summary>
    /// Maximum number of retry attempts. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Initial delay in seconds before the first retry. Defaults to 2.
    /// </summary>
    public int DelaySeconds { get; init; } = 2;

    /// <summary>
    /// Multiplier for exponential backoff (e.g 2 = 2s, 4s, 8s). Defaults to 2.
    /// </summary>
    public int BackoffMultiplier { get; init; } = 2;
}
