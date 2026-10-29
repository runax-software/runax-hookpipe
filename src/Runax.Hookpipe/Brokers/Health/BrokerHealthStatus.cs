namespace Runax.Hookpipe.Brokers.Health;

/// <summary>
/// Health status reported by a broker connectivity probe.
/// </summary>
public enum BrokerHealthStatus
{
    /// <summary>The broker probe succeeded.</summary>
    Healthy,

    /// <summary>The broker probe failed or timed out.</summary>
    Unhealthy,

    /// <summary>The broker does not implement <see cref="IBrokerHealthCheck"/> and was not probed.</summary>
    Unknown,
}
