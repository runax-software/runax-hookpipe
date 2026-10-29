using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Brokers.Health;

namespace Runax.Hookpipe.Tests.Brokers.Health;

public sealed class BrokerHealthEndpointsTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ProbeAllAsync_BrokerWithoutHealthCheck_ReportsUnknown()
    {
        var brokers = Brokers(("plain", new PlainBroker()));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        results.Should().ContainSingle()
            .Which.Health.Status.Should().Be(BrokerHealthStatus.Unknown);
    }

    [Fact]
    public async Task ProbeAllAsync_HealthyBroker_ReportsHealthy()
    {
        var brokers = Brokers(("ok", new ProbeBroker(new BrokerHealth(BrokerHealthStatus.Healthy))));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        results.Single().Health.Status.Should().Be(BrokerHealthStatus.Healthy);
    }

    [Fact]
    public async Task ProbeAllAsync_UnhealthyBroker_ReportsUnhealthyWithDetail()
    {
        var brokers = Brokers(("bad", new ProbeBroker(new BrokerHealth(BrokerHealthStatus.Unhealthy, "down"))));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(BrokerHealthStatus.Unhealthy);
        health.Detail.Should().Be("down");
    }

    [Fact]
    public async Task ProbeAllAsync_ThrowingBroker_ReportsUnhealthyWithExceptionMessage()
    {
        var brokers = Brokers(("boom", new ProbeBroker(_ => throw new InvalidOperationException("kaboom"))));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(BrokerHealthStatus.Unhealthy);
        health.Detail.Should().Be("kaboom");
    }

    [Fact]
    public async Task ProbeAllAsync_SlowBroker_TimesOutAsUnhealthy()
    {
        var brokers = Brokers(("slow", new ProbeBroker(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new BrokerHealth(BrokerHealthStatus.Healthy);
        })));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(BrokerHealthStatus.Unhealthy);
        health.Detail.Should().Be("probe timed out");
    }

    [Fact]
    public async Task ProbeAllAsync_ProbesEveryBroker()
    {
        var brokers = Brokers(
            ("a", new ProbeBroker(new BrokerHealth(BrokerHealthStatus.Healthy))),
            ("b", new ProbeBroker(new BrokerHealth(BrokerHealthStatus.Unhealthy))),
            ("c", new PlainBroker()));

        var results = await BrokerHealthEndpoints.ProbeAllAsync(brokers, ShortTimeout, CancellationToken.None);

        results.Select(r => r.Id).Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public void AreAllBrokersReady_HealthyAndUnknownOnly_IsReady()
    {
        var results = new (string, BrokerHealth)[]
        {
            ("a", new BrokerHealth(BrokerHealthStatus.Healthy)),
            ("b", new BrokerHealth(BrokerHealthStatus.Unknown)),
        };

        BrokerHealthEndpoints.AreAllBrokersReady(results).Should().BeTrue();
    }

    [Fact]
    public void AreAllBrokersReady_AnyUnhealthy_IsNotReady()
    {
        var results = new (string, BrokerHealth)[]
        {
            ("a", new BrokerHealth(BrokerHealthStatus.Healthy)),
            ("b", new BrokerHealth(BrokerHealthStatus.Unhealthy, "down")),
        };

        BrokerHealthEndpoints.AreAllBrokersReady(results).Should().BeFalse();
    }

    private static Dictionary<string, IBroker> Brokers(params (string Id, IBroker Broker)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Broker);

    private sealed class PlainBroker : IBroker
    {
        public string Type => "plain";

        public Task PublishAsync(
            string? topic, MessageEnvelope message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ProbeBroker(Func<CancellationToken, Task<BrokerHealth>> probe) : IBroker, IBrokerHealthCheck
    {
        public ProbeBroker(BrokerHealth result) : this(_ => Task.FromResult(result)) { }

        public string Type => "probe";

        public Task PublishAsync(
            string? topic, MessageEnvelope message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            probe(cancellationToken);
    }
}
