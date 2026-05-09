using Prometheus;

namespace Hookpipe.Core.Metrics;

/// <summary>
/// Prometheus metrics for Hookpipe.
/// All counters and histograms are defined as static singletons.
/// </summary>
public static class HookpipeMetrics
{
    /// <summary>
    /// Total requests received, labeled by endpoint, method, and status code.
    /// </summary>
    public static readonly Counter RequestsTotal = Prometheus.Metrics.CreateCounter(
        "hookpipe_requests_total",
        "Total webhook requests received",
        new CounterConfiguration { LabelNames = ["endpoint_id", "method", "status_code"] });

    /// <summary>
    /// Total messages successfully produced to sinks.
    /// </summary>
    public static readonly Counter MessagesProducedTotal = Prometheus.Metrics.CreateCounter(
        "hookpipe_messages_produced_total",
        "Total messages produced to sinks",
        new CounterConfiguration { LabelNames = ["endpoint_id", "sink_id"] });

    /// <summary>
    /// Total sink errors during message production.
    /// </summary>
    public static readonly Counter SinkErrorsTotal = Prometheus.Metrics.CreateCounter(
        "hookpipe_sink_errors_total",
        "Total sink errors",
        new CounterConfiguration { LabelNames = ["endpoint_id", "sink_id"] });

    /// <summary>
    /// Total validation failures.
    /// </summary>
    public static readonly Counter ValidationFailuresTotal = Prometheus.Metrics.CreateCounter(
        "hookpipe_validation_failures_total",
        "Total validation failures",
        new CounterConfiguration { LabelNames = ["endpoint_id", "validator_type"] });

    /// <summary>
    /// Request processing duration in seconds.
    /// </summary>
    public static readonly Histogram RequestDuration = Prometheus.Metrics.CreateHistogram(
        "hookpipe_request_duration_seconds",
        "Request processing duration",
        new HistogramConfiguration { LabelNames = ["endpoint_id"] });
}
