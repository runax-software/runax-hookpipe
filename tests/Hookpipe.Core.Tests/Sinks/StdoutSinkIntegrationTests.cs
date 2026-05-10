using FluentAssertions;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class StdoutSinkIntegrationTests
{
    [Fact]
    public async Task ProduceAsync_WithBody_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<StdoutSink>>();
        var sink = new StdoutSink(logger);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "stdout-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "stdout" },
        };

        var act = () => sink.ProduceAsync(envelope);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProduceAsync_NullBody_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<StdoutSink>>();
        var sink = new StdoutSink(logger);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "stdout-null-body",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "GET",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        var act = () => sink.ProduceAsync(envelope);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProduceAsync_LogsMessage()
    {
        var logger = Substitute.For<ILogger<StdoutSink>>();
        var sink = new StdoutSink(logger);

        var envelope = new MessageEnvelope
        {
            Id = "log-test-id",
            EndpointId = "log-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await sink.ProduceAsync(envelope);

        logger.ReceivedCalls().Should().NotBeEmpty();
    }
}
