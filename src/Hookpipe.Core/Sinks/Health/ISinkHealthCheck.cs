namespace Hookpipe.Core.Sinks.Health;

/// <summary>
/// Health status reported by a sink connectivity probe.
/// </summary>
public enum SinkHealthStatus
{
    /// <summary>The sink probe succeeded.</summary>
    Healthy,

    /// <summary>The sink probe failed or timed out.</summary>
    Unhealthy,

    /// <summary>The sink does not implement <see cref="ISinkHealthCheck"/> and was not probed.</summary>
    Unknown,
}

/// <summary>
/// Result of a single sink health probe.
/// </summary>
/// <param name="Status">The probe outcome.</param>
/// <param name="Detail">Optional human-readable detail (e.g. an error message).</param>
public readonly record struct SinkHealth(SinkHealthStatus Status, string? Detail = null);

/// <summary>
/// Optionally implemented by sinks that can verify connectivity to their backing service.
/// Probes run live on each call — results must not be cached by the sink.
/// </summary>
public interface ISinkHealthCheck
{
    /// <summary>
    /// Performs a live connectivity check against the sink's backing service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token; callers apply a per-probe timeout.</param>
    /// <returns>The current health of the sink.</returns>
    Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
