using FluentAssertions;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Sinks.Health;

namespace Hookpipe.Core.Tests.Sinks.Health;

public sealed class SinkHealthEndpointsTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ProbeAllAsync_SinkWithoutHealthCheck_ReportsUnknown()
    {
        var sinks = Sinks(("plain", new PlainSink()));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        results.Should().ContainSingle()
            .Which.Health.Status.Should().Be(SinkHealthStatus.Unknown);
    }

    [Fact]
    public async Task ProbeAllAsync_HealthySink_ReportsHealthy()
    {
        var sinks = Sinks(("ok", new ProbeSink(new SinkHealth(SinkHealthStatus.Healthy))));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        results.Single().Health.Status.Should().Be(SinkHealthStatus.Healthy);
    }

    [Fact]
    public async Task ProbeAllAsync_UnhealthySink_ReportsUnhealthyWithDetail()
    {
        var sinks = Sinks(("bad", new ProbeSink(new SinkHealth(SinkHealthStatus.Unhealthy, "down"))));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(SinkHealthStatus.Unhealthy);
        health.Detail.Should().Be("down");
    }

    [Fact]
    public async Task ProbeAllAsync_ThrowingSink_ReportsUnhealthyWithExceptionMessage()
    {
        var sinks = Sinks(("boom", new ProbeSink(_ => throw new InvalidOperationException("kaboom"))));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(SinkHealthStatus.Unhealthy);
        health.Detail.Should().Be("kaboom");
    }

    [Fact]
    public async Task ProbeAllAsync_SlowSink_TimesOutAsUnhealthy()
    {
        var sinks = Sinks(("slow", new ProbeSink(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new SinkHealth(SinkHealthStatus.Healthy);
        })));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        var health = results.Single().Health;
        health.Status.Should().Be(SinkHealthStatus.Unhealthy);
        health.Detail.Should().Be("probe timed out");
    }

    [Fact]
    public async Task ProbeAllAsync_ProbesEverySink()
    {
        var sinks = Sinks(
            ("a", new ProbeSink(new SinkHealth(SinkHealthStatus.Healthy))),
            ("b", new ProbeSink(new SinkHealth(SinkHealthStatus.Unhealthy))),
            ("c", new PlainSink()));

        var results = await SinkHealthEndpoints.ProbeAllAsync(sinks, ShortTimeout, CancellationToken.None);

        results.Select(r => r.Id).Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public void AreAllSinksReady_HealthyAndUnknownOnly_IsReady()
    {
        var results = new (string, SinkHealth)[]
        {
            ("a", new SinkHealth(SinkHealthStatus.Healthy)),
            ("b", new SinkHealth(SinkHealthStatus.Unknown)),
        };

        SinkHealthEndpoints.AreAllSinksReady(results).Should().BeTrue();
    }

    [Fact]
    public void AreAllSinksReady_AnyUnhealthy_IsNotReady()
    {
        var results = new (string, SinkHealth)[]
        {
            ("a", new SinkHealth(SinkHealthStatus.Healthy)),
            ("b", new SinkHealth(SinkHealthStatus.Unhealthy, "down")),
        };

        SinkHealthEndpoints.AreAllSinksReady(results).Should().BeFalse();
    }

    private static Dictionary<string, ISink> Sinks(params (string Id, ISink Sink)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Sink);

    private sealed class PlainSink : ISink
    {
        public string Type => "plain";

        public Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ProbeSink(Func<CancellationToken, Task<SinkHealth>> probe) : ISink, ISinkHealthCheck
    {
        public ProbeSink(SinkHealth result) : this(_ => Task.FromResult(result)) { }

        public string Type => "probe";

        public Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            probe(cancellationToken);
    }
}
