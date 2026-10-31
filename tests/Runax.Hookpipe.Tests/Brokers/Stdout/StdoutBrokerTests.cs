using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Runax.Hookpipe.Brokers.Stdout;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Stdout;

public sealed class StdoutBrokerTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy()
    {
        var broker = new StdoutBroker(Substitute.For<ILogger<StdoutBroker>>());

        var health = await broker.CheckHealthAsync();

        health.Status.ShouldBe(BrokerHealthStatus.Healthy);
    }

    [Fact]
    public void Type_ReturnsStdout()
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);

        broker.Type.ShouldBe("stdout");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ignored-topic")]
    public async Task ProduceAsync_AnyTopic_DoesNotThrow(string? topic)
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);
        var envelope = new MessageEnvelope
        {
            Id = "test-id",
            WebhookId = "test-webhook",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1"
        };

        var act = () => broker.PublishAsync(topic, envelope);

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task ProduceAsync_LogsMessage()
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);
        var envelope = new MessageEnvelope
        {
            Id = "test-id",
            WebhookId = "my-webhook",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1"
        };

        await broker.PublishAsync(null, envelope);

        logger.ReceivedCalls().ShouldNotBeEmpty();
    }
}
