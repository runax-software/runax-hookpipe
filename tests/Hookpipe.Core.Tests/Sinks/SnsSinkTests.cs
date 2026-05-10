using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class SnsSinkTests : IDisposable
{
    private const string EnvVar = "TEST_SNS_TOPIC_ARN";
    private const string RegionEnvVar = "TEST_AWS_REGION";
    private const string ServiceUrlEnvVar = "TEST_AWS_SERVICE_URL";

    public SnsSinkTests()
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
        Type = "sns",
        Settings = settings ?? new()
        {
            ["topic_arn_env"] = EnvVar,
            ["region_env"] = RegionEnvVar,
            ["service_url_env"] = ServiceUrlEnvVar,
        },
    };

    [Fact]
    public void Type_ReturnsSns()
    {
        Environment.SetEnvironmentVariable(EnvVar, "arn:aws:sns:us-east-1:000000000000:test-topic");

        var sink = SnsSink.Create(MakeConfig(), Substitute.For<ILogger<SnsSink>>());

        sink.Type.Should().Be("sns");
        sink.Dispose();
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => SnsSink.Create(
            MakeConfig(new() { ["topic_arn_env"] = "NONEXISTENT_VAR", ["region_env"] = RegionEnvVar, ["service_url_env"] = ServiceUrlEnvVar }),
            Substitute.For<ILogger<SnsSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }
}
