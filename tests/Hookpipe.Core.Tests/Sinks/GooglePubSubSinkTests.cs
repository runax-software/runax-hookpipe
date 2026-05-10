using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

public sealed class GooglePubSubSinkTests : IDisposable
{
    private const string ProjectEnv = "TEST_GOOGLE_PROJECT";
    private const string TopicEnv = "TEST_PUBSUB_TOPIC";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ProjectEnv, null);
        Environment.SetEnvironmentVariable(TopicEnv, null);
        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", null);
    }

    [Fact]
    public void Create_MissingProjectEnvVar_Throws()
    {
        var act = () => GooglePubSubSink.CreateAsync(
            new SinkConfig
            {
                Id = "test",
                Type = "google-pubsub",
                Settings = new()
                {
                    ["project_id_env"] = "NONEXISTENT_PROJECT",
                    ["topic_id_env"] = TopicEnv,
                }
            },
            Substitute.For<ILogger<GooglePubSubSink>>());

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }

    [Fact]
    public void Create_MissingTopicEnvVar_Throws()
    {
        Environment.SetEnvironmentVariable(ProjectEnv, "test-project");

        var act = () => GooglePubSubSink.CreateAsync(
            new SinkConfig
            {
                Id = "test",
                Type = "google-pubsub",
                Settings = new()
                {
                    ["project_id_env"] = ProjectEnv,
                    ["topic_id_env"] = "NONEXISTENT_TOPIC",
                }
            },
            Substitute.For<ILogger<GooglePubSubSink>>());

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*env var*not set*");
    }
}
