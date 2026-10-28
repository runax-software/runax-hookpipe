namespace Runax.Hookpipe.Diagnostics;

/// <summary>
/// Names hosts use to collect Hookpipe's telemetry, for example
/// <c>metrics.AddMeter(HookpipeDiagnostics.MeterName)</c> with OpenTelemetry.
/// </summary>
public static class HookpipeDiagnostics
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> Hookpipe records metrics on.
    /// </summary>
    public const string MeterName = "Runax.Hookpipe";
}
