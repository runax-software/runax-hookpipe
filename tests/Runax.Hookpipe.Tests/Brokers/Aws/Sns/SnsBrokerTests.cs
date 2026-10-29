using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.Sns;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Brokers.Aws.Sns;

public sealed class SnsBrokerTests
{
    private static SnsBrokerSettings MakeSettings() => new()
    {
        Region = "us-east-1",
        ServiceUrl = "http://localhost:4566",
        TopicArnMap = new()
        {
            ["orders"] = "arn:aws:sns:us-east-1:000000000000:test-topic",
        },
    };

    private static SnsBroker CreateBroker() =>
        SnsBroker.Create("test", MakeSettings(), Substitute.For<ILogger<SnsBroker>>());

    [Fact]
    public void Type_ReturnsSns()
    {
        using var broker = CreateBroker();

        broker.Type.Should().Be("sns");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProduceAsync_NoTopic_ThrowsArgumentException(string? topic)
    {
        using var broker = CreateBroker();

        var act = () => broker.PublishAsync(topic, MakeEnvelope());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*needs a topic*");
    }

    [Fact]
    public async Task ProduceAsync_TopicNotInMap_ThrowsArgumentException()
    {
        using var broker = CreateBroker();

        var act = () => broker.PublishAsync("invoices", MakeEnvelope());

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*no topic ARN for topic 'invoices'*");
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
