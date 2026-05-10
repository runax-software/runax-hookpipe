using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class RedisStreamSinkTests : IDisposable
{
    private const string EnvVar = "TEST_REDIS_CONNECTION";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    [Fact]
    public void Create_MissingEnvVar_Throws()
    {
        var act = () => RedisStreamSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "redis-stream",
                Settings = new() { ["connection_env"] = "NONEXISTENT_VAR", ["stream_key"] = "test-stream" }
            },
            Substitute.For<ILogger<RedisStreamSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_MissingStreamKey_Throws()
    {
        Environment.SetEnvironmentVariable(EnvVar, "localhost:6379");

        var act = () => RedisStreamSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "redis-stream",
                Settings = new() { ["connection_env"] = EnvVar }
            },
            Substitute.For<ILogger<RedisStreamSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*stream_key*required*");
    }

    [Fact]
    public void Create_EmptyStreamKey_Throws()
    {
        Environment.SetEnvironmentVariable(EnvVar, "localhost:6379");

        var act = () => RedisStreamSink.Create(
            new SinkConfig
            {
                Id = "test",
                Type = "redis-stream",
                Settings = new() { ["connection_env"] = EnvVar, ["stream_key"] = "" }
            },
            Substitute.For<ILogger<RedisStreamSink>>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*stream_key*required*");
    }
}
