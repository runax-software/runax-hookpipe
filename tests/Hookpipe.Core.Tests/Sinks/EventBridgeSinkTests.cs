using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

[Collection("AWS")]
public sealed class EventBridgeSinkTests : IDisposable
{
    private const string EnvVar = "TEST_EVENTBRIDGE_BUS";
    private const string RegionEnvVar = "TEST_AWS_REGION";
    private const string ServiceUrlEnvVar = "TEST_AWS_SERVICE_URL";

    public EventBridgeSinkTests()
    {
        Environment.SetEnvironmentVariable(RegionEnvVar, "us-east-1");
        Environment.SetEnvironmentVariable(ServiceUrlEnvVar, "http://localhost:4566");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
        Environment.SetEnvironmentVariable(RegionEnvVar, null);
        Environment.SetEnvironmentVariable(ServiceUrlEnvVar, null);
    }

    private static SinkConfig MakeConfig(Dictionary<string, string>? settings = null) => new()
    {
        Id = "test",
        Type = "eventbridge",
        Settings = settings ?? new()
        {
            ["event_bus_env"] = EnvVar,
            ["region_env"] = RegionEnvVar,
            ["service_url_env"] = ServiceUrlEnvVar,
        },
    };

    [Fact]
    public void Type_ReturnsEventbridge()
    {
        Environment.SetEnvironmentVariable(EnvVar, "test-bus");

        var sink = EventBridgeSink.Create(MakeConfig(), Substitute.For<ILogger<EventBridgeSink>>());

        sink.Type.Should().Be("eventbridge");
        sink.Dispose();
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => EventBridgeSink.Create(
            MakeConfig(new() { ["event_bus_env"] = "NONEXISTENT_VAR", ["region_env"] = RegionEnvVar, ["service_url_env"] = ServiceUrlEnvVar }),
            Substitute.For<ILogger<EventBridgeSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_CustomSourceAndDetailType_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(EnvVar, "test-bus");

        var act = () => EventBridgeSink.Create(
            MakeConfig(new()
            {
                ["event_bus_env"] = EnvVar,
                ["region_env"] = RegionEnvVar,
                ["service_url_env"] = ServiceUrlEnvVar,
                ["source"] = "my-app",
                ["detail_type"] = "my-event",
            }),
            Substitute.For<ILogger<EventBridgeSink>>());

        act.Should().NotThrow();
    }
}
