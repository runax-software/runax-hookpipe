using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class EventHubSinkTests : IDisposable
{
    private const string EnvVar = "TEST_EVENTHUB_CONNECTION_STRING";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => EventHubSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "eventhub",
                Settings = new()
                {
                    ["connection_string_env"] = "NONEXISTENT_VAR",
                    ["event_hub_name"] = "test-hub",
                }
            },
            Substitute.For<ILogger<EventHubSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_MissingEventHubName_Throws()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var act = () => EventHubSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "eventhub",
                Settings = new() { ["connection_string_env"] = EnvVar }
            },
            Substitute.For<ILogger<EventHubSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*event_hub_name*required*");
    }

    [Fact]
    public void Create_ValidConfig_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var act = () => EventHubSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "eventhub",
                Settings = new()
                {
                    ["connection_string_env"] = EnvVar,
                    ["event_hub_name"] = "test-hub",
                }
            },
            Substitute.For<ILogger<EventHubSink>>());

        act.Should().NotThrow();
    }

    [Fact]
    public void Type_ReturnsEventhub()
    {
        Environment.SetEnvironmentVariable(EnvVar, "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test");

        var sink = EventHubSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "eventhub",
                Settings = new()
                {
                    ["connection_string_env"] = EnvVar,
                    ["event_hub_name"] = "test-hub",
                }
            },
            Substitute.For<ILogger<EventHubSink>>());

        sink.Type.Should().Be("eventhub");
    }
}
