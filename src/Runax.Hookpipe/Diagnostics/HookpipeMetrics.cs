using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Runax.Hookpipe.Diagnostics;

/// <summary>
/// Hookpipe's instruments on the <see cref="HookpipeDiagnostics.MeterName"/> meter. Hosts choose how
/// to export them: OpenTelemetry, or prometheus-net as the self-hosted service does.
/// </summary>
/// <remarks>
/// Instrument names use dots and units, following OpenTelemetry. A Prometheus exporter turns them
/// into the familiar names: <c>hookpipe.requests</c> becomes <c>hookpipe_requests_total</c>, and
/// <c>hookpipe.request.duration</c> (unit <c>s</c>) becomes <c>hookpipe_request_duration_seconds</c>.
/// </remarks>
internal static class HookpipeMetrics
{
    private static readonly Meter Meter = new(HookpipeDiagnostics.MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "hookpipe.requests", description: "Total webhook requests received");

    private static readonly Counter<long> MessagesProduced = Meter.CreateCounter<long>(
        "hookpipe.messages.produced", description: "Total messages produced to sinks");

    private static readonly Counter<long> SinkErrors = Meter.CreateCounter<long>(
        "hookpipe.sink.errors", description: "Total sink errors");

    private static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>(
        "hookpipe.validation.failures", description: "Total validation failures");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "hookpipe.request.duration", unit: "s", description: "Request processing duration");

    /// <summary>
    /// Counts a finished request by endpoint, method, and status code.
    /// </summary>
    public static void RecordRequest(string endpointId, string method, string statusCode) =>
        Requests.Add(1, new("endpoint_id", endpointId), new("method", method), new("status_code", statusCode));

    /// <summary>
    /// Counts a message produced to a sink.
    /// </summary>
    public static void RecordMessageProduced(string endpointId, string sinkId) =>
        MessagesProduced.Add(1, new("endpoint_id", endpointId), new("sink_id", sinkId));

    /// <summary>
    /// Counts a sink that failed to produce a message.
    /// </summary>
    public static void RecordSinkError(string endpointId, string sinkId) =>
        SinkErrors.Add(1, new("endpoint_id", endpointId), new("sink_id", sinkId));

    /// <summary>
    /// Counts a request rejected by validation.
    /// </summary>
    public static void RecordValidationFailure(string endpointId, string validatorType) =>
        ValidationFailures.Add(1, new("endpoint_id", endpointId), new("validator_type", validatorType));

    /// <summary>
    /// Starts timing a request; disposing the result records its duration.
    /// </summary>
    public static RequestTimer TimeRequest(string endpointId) => new(endpointId, Stopwatch.GetTimestamp());

    /// <summary>
    /// Records a request's duration when disposed.
    /// </summary>
    public readonly struct RequestTimer(string endpointId, long startTimestamp) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() =>
            RequestDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, new KeyValuePair<string, object?>("endpoint_id", endpointId));
    }
}
