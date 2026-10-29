namespace Runax.Hookpipe.Brokers.Health;

/// <summary>
/// Optionally implemented by brokers that can verify connectivity to their backing service.
/// Probes run live on each call — results must not be cached by the broker.
/// </summary>
public interface IBrokerHealthCheck
{
    /// <summary>
    /// Performs a live connectivity check against the broker's backing service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token; callers apply a per-probe timeout.</param>
    /// <returns>The current health of the broker.</returns>
    Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
