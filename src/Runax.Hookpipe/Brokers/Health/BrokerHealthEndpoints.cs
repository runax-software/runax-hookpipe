using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Runax.Hookpipe.Brokers.Health;

/// <summary>
/// Endpoint mapping for broker health checks.
/// </summary>
public static class BrokerHealthEndpoints
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Maps an on-demand readiness endpoint that probes every broker implementing
    /// <see cref="IBrokerHealthCheck"/> in parallel. Returns 200 with per-broker status,
    /// or 503 if any broker is unhealthy. Probes run live on each request — never cached.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="brokers">The active brokers keyed by broker ID.</param>
    /// <param name="pattern">The route pattern. Defaults to <c>/health/brokers</c>.</param>
    /// <returns>The endpoint convention builder for further configuration.</returns>
    public static IEndpointConventionBuilder MapBrokerHealthChecks(
        this IEndpointRouteBuilder endpoints,
        IReadOnlyDictionary<string, IBroker> brokers,
        string pattern = "/health/brokers")
    {
        return endpoints.MapGet(pattern, async (CancellationToken requestAborted) =>
        {
            var results = await ProbeAllAsync(brokers, ProbeTimeout, requestAborted);

            var body = results.ToDictionary(
                result => result.Id,
                result => new
                { status = result.Health.Status.ToString().ToLowerInvariant(), detail = result.Health.Detail });

            return AreAllBrokersReady(results)
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    /// <summary>
    /// Probes every broker in parallel, applying <paramref name="probeTimeout"/> to each probe.
    /// Brokers not implementing <see cref="IBrokerHealthCheck"/> report <see cref="BrokerHealthStatus.Unknown"/>.
    /// </summary>
    internal static async Task<IReadOnlyList<(string Id, BrokerHealth Health)>> ProbeAllAsync(
        IReadOnlyDictionary<string, IBroker> brokers, TimeSpan probeTimeout, CancellationToken requestAborted)
    {
        var probes = brokers.Select(kv => ProbeAsync(kv.Key, kv.Value, probeTimeout, requestAborted));
        return await Task.WhenAll(probes);
    }

    /// <summary>
    /// Readiness rule: ready unless any broker is <see cref="BrokerHealthStatus.Unhealthy"/>.
    /// <see cref="BrokerHealthStatus.Unknown"/> (unprobed) does not fail readiness.
    /// </summary>
    internal static bool AreAllBrokersReady(IEnumerable<(string Id, BrokerHealth Health)> results) =>
        results.All(result => result.Health.Status != BrokerHealthStatus.Unhealthy);

    private static async Task<(string Id, BrokerHealth Health)> ProbeAsync(
        string id, IBroker broker, TimeSpan probeTimeout, CancellationToken requestAborted)
    {
        if (broker is not IBrokerHealthCheck check)
            return (id, new BrokerHealth(BrokerHealthStatus.Unknown));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(probeTimeout);

        try
        {
            return (id, await check.CheckHealthAsync(cts.Token));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            return (id, new BrokerHealth(BrokerHealthStatus.Unhealthy, "probe timed out"));
        }
        catch (Exception ex)
        {
            return (id, new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message));
        }
    }
}
