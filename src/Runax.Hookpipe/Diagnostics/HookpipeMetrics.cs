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

    private static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>(
        "hookpipe.messages.published", description: "Total messages published to brokers");

    private static readonly Counter<long> PublishErrors = Meter.CreateCounter<long>(
        "hookpipe.publish.errors", description: "Total failed publishes");

    private static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>(
        "hookpipe.validation.failures", description: "Total validation failures");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "hookpipe.request.duration", unit: "s", description: "Request processing duration");

    /// <summary>
    /// Counts a finished request by webhook, method, and status code.
    /// </summary>
    public static void RecordRequest(string webhookId, string method, string statusCode) =>
        Requests.Add(1, new("webhook_id", webhookId), new("method", method), new("status_code", statusCode));

    /// <summary>
    /// Counts a message published to a broker.
    /// </summary>
    public static void RecordMessagePublished(string webhookId, string brokerId) =>
        MessagesPublished.Add(1, new("webhook_id", webhookId), new("broker_id", brokerId));

    /// <summary>
    /// Counts a publish to a broker that failed.
    /// </summary>
    public static void RecordPublishError(string webhookId, string brokerId) =>
        PublishErrors.Add(1, new("webhook_id", webhookId), new("broker_id", brokerId));

    /// <summary>
    /// Counts a request rejected by validation.
    /// </summary>
    public static void RecordValidationFailure(string webhookId, string validatorType) =>
        ValidationFailures.Add(1, new("webhook_id", webhookId), new("validator_type", validatorType));

    /// <summary>
    /// Starts timing a request; disposing the result records its duration.
    /// </summary>
    public static RequestTimer TimeRequest(string webhookId) => new(webhookId, Stopwatch.GetTimestamp());

    /// <summary>
    /// Records a request's duration when disposed.
    /// </summary>
    public readonly struct RequestTimer(string webhookId, long startTimestamp) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() =>
            RequestDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, new KeyValuePair<string, object?>("webhook_id", webhookId));
    }
}
