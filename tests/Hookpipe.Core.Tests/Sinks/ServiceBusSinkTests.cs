using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class ServiceBusSinkTests : IDisposable
{
    private const string EnvVar = "TEST_SERVICEBUS_CONNECTION_STRING";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => ServiceBusSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "servicebus",
                Settings = new()
                {
                    ["connection_string_env"] = "NONEXISTENT_VAR",
                    ["queue_or_topic"] = "test-queue",
                }
            },
            Substitute.For<ILogger<ServiceBusSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_MissingQueueOrTopic_Throws()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var act = () => ServiceBusSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "servicebus",
                Settings = new() { ["connection_string_env"] = EnvVar }
            },
            Substitute.For<ILogger<ServiceBusSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*queue_or_topic*required*");
    }

    [Fact]
    public void Create_ValidConfig_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var act = () => ServiceBusSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "servicebus",
                Settings = new()
                {
                    ["connection_string_env"] = EnvVar,
                    ["queue_or_topic"] = "test-queue",
                }
            },
            Substitute.For<ILogger<ServiceBusSink>>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Type_ReturnsServicebus()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var sink = ServiceBusSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "servicebus",
                Settings = new()
                {
                    ["connection_string_env"] = EnvVar,
                    ["queue_or_topic"] = "test-queue",
                }
            },
            Substitute.For<ILogger<ServiceBusSink>>());

        sink.Type.Should().Be("servicebus");
    }
}
