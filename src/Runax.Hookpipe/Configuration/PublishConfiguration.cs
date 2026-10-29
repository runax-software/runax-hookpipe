namespace Runax.Hookpipe.Configuration;

/// <summary>
/// How Hookpipe publishes to a broker, the <c>brokers.&lt;id&gt;.publish</c> section.
/// </summary>
public sealed class PublishConfiguration
{
    /// <summary>
    /// The retry policy for failed publishes. Null means no retries.
    /// </summary>
    public RetryConfiguration? Retry { get; init; }
}
