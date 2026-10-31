using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.EventBridge;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Aws.EventBridge;

public sealed class EventBridgeBrokerTests
{
    private static EventBridgeBrokerSettings MakeSettings(string? source = null)
    {
        var settings = new EventBridgeBrokerSettings
        {
            EventBusName = "test-bus",
            Region = "us-east-1",
            ServiceUrl = "http://localhost:4566",
        };

        if (source is not null) settings.Source = source;
        return settings;
    }

    private static EventBridgeBroker CreateBroker(EventBridgeBrokerSettings? settings = null) =>
        EventBridgeBroker.Create("test", settings ?? MakeSettings(), Substitute.For<ILogger<EventBridgeBroker>>());

    [Fact]
    public void Type_ReturnsEventbridge()
    {
        using var broker = CreateBroker();

        broker.Type.ShouldBe("eventbridge");
    }

    [Fact]
    public void Create_CustomSource_DoesNotThrow()
    {
        var act = () => CreateBroker(MakeSettings(source: "my-app"));

        Should.NotThrow(act).Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProduceAsync_NoTopic_ThrowsArgumentException(string? topic)
    {
        using var broker = CreateBroker();

        var act = () => broker.PublishAsync(topic, MakeEnvelope());

        var exception = await Should.ThrowAsync<ArgumentException>(act);
        exception.Message.ShouldContain("needs a topic");
    }

    private static MessageEnvelope MakeEnvelope() => new()
    {
        Id = "test-id",
        WebhookId = "test-webhook",
        ReceivedAt = DateTimeOffset.UtcNow,
        Method = "POST",
        Path = "/test",
        RemoteAddress = "127.0.0.1",
    };
}
