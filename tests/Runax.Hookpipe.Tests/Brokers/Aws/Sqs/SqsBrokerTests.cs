using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.Sqs;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Aws.Sqs;

public sealed class SqsBrokerTests
{
    private static SqsBrokerSettings MakeSettings(string region = "us-east-1") => new()
    {
        Region = region,
        // A custom endpoint keeps the SDK away from real AWS credentials.
        ServiceUrl = "http://localhost:4566",
        TopicQueueUrlMap = new()
        {
            ["orders"] = $"https://sqs.{region}.amazonaws.com/123456789/test-queue",
        },
    };

    private static SqsBroker CreateBroker(SqsBrokerSettings? settings = null) =>
        SqsBroker.Create("test", settings ?? MakeSettings(), Substitute.For<ILogger<SqsBroker>>());

    [Fact]
    public void Type_ReturnsSqs()
    {
        using var broker = CreateBroker();

        broker.Type.ShouldBe("sqs");
    }

    [Fact]
    public void Create_WithRegion_DoesNotThrow()
    {
        var act = () => CreateBroker(MakeSettings("eu-west-1"));

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

    [Fact]
    public async Task ProduceAsync_TopicNotInMap_ThrowsArgumentException()
    {
        using var broker = CreateBroker();

        var act = () => broker.PublishAsync("invoices", MakeEnvelope());

        var exception = await Should.ThrowAsync<ArgumentException>(act);
        exception.Message.ShouldContain("no queue URL for topic 'invoices'");
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
