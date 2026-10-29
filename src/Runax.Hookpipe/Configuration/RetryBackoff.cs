namespace Runax.Hookpipe.Configuration;

/// <summary>
/// How a retry delay grows.
/// </summary>
public enum RetryBackoff
{
    /// <summary>Every retry waits <see cref="RetryConfiguration.Delay"/>.</summary>
    Constant,

    /// <summary>Retry <c>n</c> waits <c>n × Delay</c>.</summary>
    Linear,

    /// <summary>Retry <c>n</c> waits <c>Delay × Factor^(n-1)</c>.</summary>
    Exponential,
}
