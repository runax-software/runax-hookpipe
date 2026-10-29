using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Stdout;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Brokers.Stdout;

public sealed class StdoutBrokerIntegrationTests
{
    [Fact]
    public async Task ProduceAsync_WithBody_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "stdout-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "stdout" },
        };

        var act = () => broker.PublishAsync(null, envelope);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProduceAsync_NullBody_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "stdout-null-body",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "GET",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        var act = () => broker.PublishAsync(null, envelope);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProduceAsync_LogsMessage()
    {
        var logger = Substitute.For<ILogger<StdoutBroker>>();
        var broker = new StdoutBroker(logger);

        var envelope = new MessageEnvelope
        {
            Id = "log-test-id",
            WebhookId = "log-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await broker.PublishAsync(null, envelope);

        logger.ReceivedCalls().Should().NotBeEmpty();
    }
}
