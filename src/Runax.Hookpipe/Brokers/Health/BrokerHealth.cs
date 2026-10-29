namespace Runax.Hookpipe.Brokers.Health;

/// <summary>
/// Result of a single broker health probe.
/// </summary>
/// <param name="Status">The probe outcome.</param>
/// <param name="Detail">Optional human-readable detail (e.g. an error message).</param>
public readonly record struct BrokerHealth(BrokerHealthStatus Status, string? Detail = null);
