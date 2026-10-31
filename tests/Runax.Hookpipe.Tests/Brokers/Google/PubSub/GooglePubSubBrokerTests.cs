using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Runax.Hookpipe.Brokers.Google.PubSub;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Google.PubSub;

public sealed class GooglePubSubBrokerTests
{
    private static GooglePubSubBroker CreateBroker() => GooglePubSubBroker.Create(
        "test",
        new GooglePubSubBrokerSettings { ProjectId = "test-project" },
        Substitute.For<ILogger<GooglePubSubBroker>>());

    [Fact]
    public async Task Type_ReturnsGooglePubSub()
    {
        await using var broker = CreateBroker();

        broker.Type.ShouldBe("google-pubsub");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProduceAsync_NoTopic_ThrowsArgumentException(string? topic)
    {
        await using var broker = CreateBroker();
        var envelope = new MessageEnvelope
        {
            Id = "test-id",
            WebhookId = "test-webhook",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        var act = () => broker.PublishAsync(topic, envelope);

        var exception = await Should.ThrowAsync<ArgumentException>(act);
        exception.Message.ShouldContain("needs a topic");
    }

    [Fact]
    public async Task DisposeAsync_NoPublishers_DoesNotThrow()
    {
        var broker = CreateBroker();

        var act = async () => await broker.DisposeAsync();

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task CheckHealthAsync_NoTopicsInUse_ReportsUnknown()
    {
        await using var broker = CreateBroker();

        var health = await broker.CheckHealthAsync();

        health.ShouldBe(new BrokerHealth(BrokerHealthStatus.Unknown, "no topic to probe"));
    }
}
