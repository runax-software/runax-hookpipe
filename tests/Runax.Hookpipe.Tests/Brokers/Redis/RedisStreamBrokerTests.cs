using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Redis;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Redis;

public sealed class RedisStreamBrokerTests
{
    // abortConnect=false lets the broker be created without a Redis server. Nothing listens on port 1,
    // and the topic is checked before any command is sent.
    private const string UnreachableConfiguration = "127.0.0.1:1,abortConnect=false,connectTimeout=500";

    private static RedisStreamBroker CreateBroker() => RedisStreamBroker.Create(
        "test",
        new RedisBrokerSettings { Configuration = UnreachableConfiguration },
        Substitute.For<ILogger<RedisStreamBroker>>());

    [Fact]
    public void Type_ReturnsRedis()
    {
        using var broker = CreateBroker();

        broker.Type.ShouldBe("redis");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProduceAsync_NoTopic_ThrowsArgumentException(string? topic)
    {
        using var broker = CreateBroker();
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
}
