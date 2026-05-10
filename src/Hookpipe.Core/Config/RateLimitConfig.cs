namespace Hookpipe.Core.Config;

/// <summary>
/// Optional rate limit configuration for an endpoint.
/// Uses a fixed window rate limiter.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>
    /// Maximum number of requests allowed in the time window. Defaults to 100.
    /// </summary>
    public int Requests { get; init; } = 100;

    /// <summary>
    /// Time Window in seconds. Defaults to 60
    /// </summary>
    public int WindowSeconds { get; init; } = 60;
}
