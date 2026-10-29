using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Runax.Hookpipe.Brokers.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using NSubstitute;

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

        await act.Should().NotThrow().Which.DisposeAsync();
    }

    [Fact]
    public async Task Type_ReturnsAzureServiceBus()
    {
        await using var broker = CreateBroker();

        broker.Type.Should().Be("azure-servicebus");
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

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*needs a topic*");
    }

    [Fact]
    public async Task CheckHealthAsync_NoTopicsInUse_ReportsUnknown()
    {
        await using var broker = CreateBroker();

        var health = await broker.CheckHealthAsync();

        health.Should().Be(new BrokerHealth(BrokerHealthStatus.Unknown, "no queue or topic to probe"));
    }
}
