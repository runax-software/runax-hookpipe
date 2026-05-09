using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class HttpRelaySinkTests : IDisposable
{
    private const string EnvVar = "TEST_HTTP_RELAY_URL";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    [Fact]
    public void Type_ReturnsHttp()
    {
        Environment.SetEnvironmentVariable(EnvVar, "http://localhost:9999");

        var sink = HttpRelaySink.Create(
            new SinkConfig { Id = "test", Type = "http", Settings = new() { ["url_env"] = EnvVar } },
            Substitute.For<ILogger<HttpRelaySink>>());

        sink.Type.Should().Be("http");
        sink.Dispose();
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => HttpRelaySink.Create(
            new SinkConfig { Id = "test", Type = "http", Settings = new() { ["url_env"] = "NONEXISTENT_VAR" } },
            Substitute.For<ILogger<HttpRelaySink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_CustomTimeout_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable(EnvVar, "http://localhost:9999");

        var act = () => HttpRelaySink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "http",
                Settings = new() { ["url_env"] = EnvVar, ["timeout_seconds"] = "5" }
            },
            Substitute.For<ILogger<HttpRelaySink>>());

        act.Should().NotThrow();
    }
}
