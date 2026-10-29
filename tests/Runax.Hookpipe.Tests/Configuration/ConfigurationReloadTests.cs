using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe.Tests.Configuration;

public sealed class ConfigurationReloadTests : IDisposable
{
    private const string Running = """
        version: 2
        brokers:
          console:
            type: stdout
          rabbit:
            type: rabbitmq
            settings:
              uri: amqp://localhost
        webhooks:
          github:
            rate_limit: { requests: 10, window: 1m }
            to: console
        """;

    private readonly string _configurationPath = Path.Combine(Path.GetTempPath(), $"hookpipe-reload-{Guid.NewGuid():N}.yaml");

    [Fact]
    public void Compare_LiveChangesOnly_NeedsNoRestart()
    {
        var check = Compare(Running.Replace("to: console", "to: rabbit:github.events"));

        check.RestartRequired.Should().BeEmpty();
        check.Rejections.Should().BeEmpty();
    }

    [Fact]
    public void Compare_StructuralChanges_ListsWhatNeedsARestart()
    {
        var check = Compare("""
            version: 2
            server:
              paths:
                webhooks: /hooks
            brokers:
              console:
                type: stdout
              rabbit:
                type: rabbitmq
                settings:
                  uri: amqp://other-host
              audit:
                type: stdout
            webhooks:
              github:
                path: gh
                to: console
              stripe:
                to: console
            """);

        check.RestartRequired.Should().BeEquivalentTo(
            "server.paths.webhooks (changed)",
            "brokers.audit (added)",
            "brokers.rabbit.settings (changed)",
            "webhooks.stripe (added)",
            "webhooks.github.path (changed)",
            "webhooks.github.rate_limit (changed)");
        check.Rejections.Should().BeEmpty();
    }

    [Fact]
    public void Compare_TargetOnABrokerThatIsNotRunning_IsRejected()
    {
        var check = Compare("""
            version: 2
            brokers:
              console:
                type: stdout
              rabbit:
                type: rabbitmq
                settings:
                  uri: amqp://localhost
              audit:
                type: stdout
            webhooks:
              github:
                rate_limit: { requests: 10, window: 1m }
                to: audit
            """);

        check.Rejections.Should().ContainSingle()
            .Which.Should().Contain("target 'audit' uses broker 'audit', which isn't running");
    }

    [Fact]
    public void Reload_RejectedChange_KeepsTheCurrentConfiguration()
    {
        File.WriteAllText(_configurationPath, Running);
        var provider = new HookpipeConfigurationProvider(_configurationPath, NullLogger<HookpipeConfigurationProvider>.Instance);

        File.WriteAllText(_configurationPath, """
            version: 2
            brokers:
              console:
                type: rabbitmq
                settings:
                  uri: amqp://localhost
            webhooks:
              github:
                to: console:events
            """);

        provider.Reload().Should().BeFalse();
        provider.Current.Brokers["console"].Type.Should().Be("stdout");
    }

    [Fact]
    public void Reload_InvalidFile_KeepsTheCurrentConfiguration()
    {
        File.WriteAllText(_configurationPath, Running);
        var provider = new HookpipeConfigurationProvider(_configurationPath, NullLogger<HookpipeConfigurationProvider>.Instance);

        File.WriteAllText(_configurationPath, "version: 2\nwebhooks:\n  github:\n    to: missing\n");

        provider.Reload().Should().BeFalse();
        provider.Current.Webhooks["github"].To.Should().Equal(new Target("console", null));
    }

    [Fact]
    public void Reload_LiveChange_AppliesIt()
    {
        File.WriteAllText(_configurationPath, Running);
        var provider = new HookpipeConfigurationProvider(_configurationPath, NullLogger<HookpipeConfigurationProvider>.Instance);

        File.WriteAllText(_configurationPath, Running.Replace("to: console", "to: rabbit:github.events"));

        provider.Reload().Should().BeTrue();
        provider.Current.Webhooks["github"].To.Should().Equal(new Target("rabbit", "github.events"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_configurationPath)) File.Delete(_configurationPath);
    }

    private static ConfigurationReloadCheck Compare(string next) =>
        ConfigurationReloadCheck.Compare(ConfigurationLoader.LoadFromString(Running), ConfigurationLoader.LoadFromString(next));
}
