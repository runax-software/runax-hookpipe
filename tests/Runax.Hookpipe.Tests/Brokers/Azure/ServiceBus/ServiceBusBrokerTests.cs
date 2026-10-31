using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Runax.Hookpipe.Brokers.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Azure.ServiceBus;

public sealed class ServiceBusBrokerTests
{
    private const string ConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test";

    private static ServiceBusBroker CreateBroker() => ServiceBusBroker.Create(
        "test",
        new ServiceBusBrokerSettings { ConnectionString = ConnectionString },
        Substitute.For<ILogger<ServiceBusBroker>>());

    [Fact]
    public async Task Create_ValidConfiguration_DoesNotThrow()
    {
        var act = () => CreateBroker();

        await Should.NotThrow(act).DisposeAsync();
    }

    [Fact]
    public async Task Type_ReturnsAzureServiceBus()
    {
        await using var broker = CreateBroker();

        broker.Type.ShouldBe("azure-servicebus");
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
    public async Task CheckHealthAsync_NoTopicsInUse_ReportsUnknown()
    {
        await using var broker = CreateBroker();

        var health = await broker.CheckHealthAsync();

        health.ShouldBe(new BrokerHealth(BrokerHealthStatus.Unknown, "no queue or topic to probe"));
    }
}
