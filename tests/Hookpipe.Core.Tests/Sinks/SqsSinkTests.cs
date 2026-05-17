using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class SqsSinkTests : IDisposable
{
    private const string EnvVar = "TEST_SQS_QUEUE_URL";
    private const string RegionEnvVar = "TEST_AWS_REGION";
    private const string ServiceUrlEnvVar = "TEST_SQS_SERVICE_URL";

    public SqsSinkTests()
    {
        Environment.SetEnvironmentVariable(RegionEnvVar, "us-east-1");
        // Use a fake service URL so the SDK doesn't try real AWS credentials
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
        Type = "sqs",
        Settings = settings ?? new()
        {
            ["queue_url_env"] = EnvVar,
            ["region_env"] = RegionEnvVar,
            ["service_url_env"] = ServiceUrlEnvVar,
        },
    };

    [Fact]
    public void Type_ReturnsSqs()
    {
        Environment.SetEnvironmentVariable(EnvVar, "https://sqs.us-east-1.amazonaws.com/123456789/test-queue");

        var sink = SqsSink.Create(MakeConfig(), Substitute.For<ILogger<SqsSink>>());

        sink.Type.Should().Be("sqs");
        sink.Dispose();
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => SqsSink.Create(
            MakeConfig(new()
            {
                ["queue_url_env"] = "NONEXISTENT_VAR",
                ["region_env"] = RegionEnvVar,
                ["service_url_env"] = ServiceUrlEnvVar,
            }),
            Substitute.For<ILogger<SqsSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_WithRegion_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(EnvVar, "https://sqs.eu-west-1.amazonaws.com/123456789/test-queue");
        Environment.SetEnvironmentVariable(RegionEnvVar, "eu-west-1");

        var act = () => SqsSink.Create(MakeConfig(), Substitute.For<ILogger<SqsSink>>());

        act.Should().NotThrow();
    }
}
