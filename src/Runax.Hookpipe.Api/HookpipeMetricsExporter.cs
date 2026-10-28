using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Prometheus;
using Runax.Hookpipe.Diagnostics;

namespace Runax.Hookpipe.Api;

/// <summary>
/// Exports the <see cref="HookpipeDiagnostics.MeterName"/> meter through prometheus-net, keeping the
/// metric names, types, labels, and buckets Hookpipe has always exported on <c>/metrics</c>.
/// </summary>
/// <remarks>
/// prometheus-net's own <c>MeterAdapter</c> isn't used because it prefixes every name with the meter
/// name and exports counters as gauges, which would break existing dashboards and <c>rate()</c> queries.
/// Names follow the OpenTelemetry Prometheus rules: dots become underscores, unit <c>s</c> adds
/// <c>_seconds</c>, and counters add <c>_total</c>.
/// </remarks>
internal sealed class HookpipeMetricsExporter : IHostedService
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<Instrument, ExportedMetric> _metrics = new();

    public HookpipeMetricsExporter()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == HookpipeDiagnostics.MeterName && (IsCounter(instrument) || IsHistogram(instrument)))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // prometheus-net enables its MeterAdapter by default, which would export this meter a second
        // time as gauges under runax_hookpipe_*. Leave it every other meter.
        Metrics.ConfigureMeterAdapter(adapter =>
            adapter.InstrumentFilterPredicate = instrument => instrument.Meter.Name != HookpipeDiagnostics.MeterName);

        _listener.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps an instrument name to its Prometheus name, e.g. <c>hookpipe.requests</c> to
    /// <c>hookpipe_requests_total</c>.
    /// </summary>
    internal static string PrometheusName(string instrumentName, string? unit, bool isCounter)
    {
        var name = instrumentName.Replace('.', '_');
        if (unit == "s") name += "_seconds";
        if (isCounter) name += "_total";
        return name;
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!_metrics.TryGetValue(instrument, out var metric))
        {
            var labelNames = new string[tags.Length];
            for (var i = 0; i < tags.Length; i++)
                labelNames[i] = tags[i].Key;

            metric = _metrics.GetOrAdd(instrument, CreateMetric(instrument, labelNames));
        }

        var labelValues = new string[metric.LabelNames.Length];
        for (var i = 0; i < labelValues.Length; i++)
            labelValues[i] = FindTag(tags, metric.LabelNames[i]);

        metric.Record(labelValues, value);
    }

    private static ExportedMetric CreateMetric(Instrument instrument, string[] labelNames)
    {
        var isCounter = IsCounter(instrument);
        var name = PrometheusName(instrument.Name, instrument.Unit, isCounter);
        var help = instrument.Description ?? name;

        return isCounter
            ? new ExportedMetric(labelNames,
                Metrics.CreateCounter(name, help, new CounterConfiguration { LabelNames = labelNames }), null)
            : new ExportedMetric(labelNames, null,
                Metrics.CreateHistogram(name, help, new HistogramConfiguration { LabelNames = labelNames }));
    }

    private static string FindTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (var tag in tags)
            if (tag.Key == name)
                return tag.Value?.ToString() ?? "";

        return "";
    }

    private static bool IsCounter(Instrument instrument) => IsGeneric(instrument, typeof(Counter<>));

    private static bool IsHistogram(Instrument instrument) => IsGeneric(instrument, typeof(Histogram<>));

    private static bool IsGeneric(Instrument instrument, Type definition) =>
        instrument.GetType().IsGenericType && instrument.GetType().GetGenericTypeDefinition() == definition;

    private sealed record ExportedMetric(string[] LabelNames, Counter? Counter, Histogram? Histogram)
    {
        public void Record(string[] labelValues, double value)
        {
            if (Counter is not null)
                Counter.WithLabels(labelValues).Inc(value);
            else
                Histogram!.WithLabels(labelValues).Observe(value);
        }
    }
}
