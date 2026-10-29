namespace Runax.Hookpipe.Configuration;

/// <summary>
/// A webhook's fixed-window rate limit, the <c>webhooks.&lt;id&gt;.rate_limit</c> section.
/// </summary>
public sealed class RateLimitConfiguration
{
    /// <summary>
    /// Requests allowed per window. Defaults to 100.
    /// </summary>
    public int Requests { get; init; } = 100;

    /// <summary>
    /// The window length. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);
}
