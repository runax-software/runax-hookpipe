using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hookpipe.Core.Sinks.Health;

/// <summary>
/// Endpoint mapping for sink health checks.
/// </summary>
public static class SinkHealthEndpoints
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Maps an on-demand readiness endpoint that probes every sink implementing
    /// <see cref="ISinkHealthCheck"/> in parallel. Returns 200 with per-sink status,
    /// or 503 if any sink is unhealthy. Probes run live on each request — never cached.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="sinks">The active sinks keyed by sink ID.</param>
    /// <param name="pattern">The route pattern. Defaults to <c>/health/sinks</c>.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapSinkHealthChecks(
        this IEndpointRouteBuilder endpoints,
        IReadOnlyDictionary<string, ISink> sinks,
        string pattern = "/health/sinks")
    {
        return endpoints.MapGet(pattern, async (CancellationToken requestAborted) =>
        {
            var results = await ProbeAllAsync(sinks, ProbeTimeout, requestAborted);

            var body = results.ToDictionary(
                result => result.Id,
                result => new
                { status = result.Health.Status.ToString().ToLowerInvariant(), detail = result.Health.Detail });

            return AreAllSinksReady(results)
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    /// <summary>
    /// Probes every sink in parallel, applying <paramref name="probeTimeout"/> to each probe.
    /// Sinks not implementing <see cref="ISinkHealthCheck"/> report <see cref="SinkHealthStatus.Unknown"/>.
    /// </summary>
    internal static async Task<IReadOnlyList<(string Id, SinkHealth Health)>> ProbeAllAsync(
        IReadOnlyDictionary<string, ISink> sinks, TimeSpan probeTimeout, CancellationToken requestAborted)
    {
        var probes = sinks.Select(kv => ProbeAsync(kv.Key, kv.Value, probeTimeout, requestAborted));
        return await Task.WhenAll(probes);
    }

    /// <summary>
    /// Readiness rule: ready unless any sink is <see cref="SinkHealthStatus.Unhealthy"/>.
    /// <see cref="SinkHealthStatus.Unknown"/> (unprobed) does not fail readiness.
    /// </summary>
    internal static bool AreAllSinksReady(IEnumerable<(string Id, SinkHealth Health)> results) =>
        results.All(result => result.Health.Status != SinkHealthStatus.Unhealthy);

    private static async Task<(string Id, SinkHealth Health)> ProbeAsync(
        string id, ISink sink, TimeSpan probeTimeout, CancellationToken requestAborted)
    {
        if (sink is not ISinkHealthCheck check)
            return (id, new SinkHealth(SinkHealthStatus.Unknown));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(probeTimeout);

        try
        {
            return (id, await check.CheckHealthAsync(cts.Token));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            return (id, new SinkHealth(SinkHealthStatus.Unhealthy, "probe timed out"));
        }
        catch (Exception ex)
        {
            return (id, new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message));
        }
    }
}
