using FluentAssertions;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class StdoutSinkTests
{
    [Fact]
    public void Type_ReturnsStdout()
    {
        var logger = Substitute.For<ILogger<StdoutSink>>();
        var sink = new StdoutSink(logger);

        sink.Type.Should().Be("stdout");
    }

    [Fact]
    public async Task ProduceAsync_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger<StdoutSink>>();
        var sink = new StdoutSink(logger);
        var envelope = new MessageEnvelope
        {
            Id = "test-id",
            EndpointId = "test-endpoint",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1"
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
            Id = "test-id",
            EndpointId = "my-endpoint",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1"
        };

        await sink.ProduceAsync(envelope);

        logger.ReceivedCalls().Should().NotBeEmpty();
    }
}
