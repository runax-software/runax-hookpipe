using Microsoft.Extensions.DependencyInjection;
using Runax.Hookpipe.Brokers.Kafka;
using Runax.Hookpipe.Brokers.RabbitMq;
using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Hosting;
using Shouldly;

namespace Runax.Hookpipe.Tests.Configuration;

/// <summary>
/// Definitions built in code produce the same model YAML does, and are checked just as strictly.
/// </summary>
public sealed class CodeDefinitionsTests
{
    [Fact]
    public void AddWebhook_WithTargets_BuildsTheSameModelAsYaml()
    {
        var fromCode = Build(hookpipe => hookpipe
            .AddBroker("console", broker => broker.Stdout())
            .AddWebhook("ping", webhook => webhook.To("console")));

        var fromYaml = ConfigurationLoader.LoadFromString("""
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              ping:
                to: console
            """);

        fromCode.Brokers["console"].Type.ShouldBe(fromYaml.Brokers["console"].Type);

        var code = fromCode.Webhooks["ping"];
        var yaml = fromYaml.Webhooks["ping"];
        code.Path.ShouldBe(yaml.Path);
        code.Methods.ShouldBe(yaml.Methods);
        code.To.ShouldBe(yaml.To);
        code.Message.Body.ShouldBe(yaml.Message.Body);
        code.Message.IncludeHeaders.ShouldBe(yaml.Message.IncludeHeaders);
    }

    [Fact]
    public void AddWebhook_FullDefinition_BuildsTheSameModelAsYaml()
    {
        var fromCode = Build(hookpipe =>
        {
            hookpipe.Paths.Webhooks = "/hooks";
            hookpipe
                .AddBroker("rabbit", broker => broker
                    .Type("rabbitmq", new RabbitMqBrokerSettings { Uri = "amqp://localhost", ExchangeName = "webhooks" })
                    .Retry(maxRetries: 5, delay: TimeSpan.FromMilliseconds(500), RetryBackoff.Linear, factor: 3))
                .AddWebhook("github", webhook => webhook
                    .ValidateHmacSha256("X-Hub-Signature-256", "s3cret")
                    .ValidateIpAllowlist("192.30.252.0/22")
                    .MaxBodySize(10 * 1024 * 1024)
                    .RateLimit(requests: 10, TimeSpan.FromSeconds(30))
                    .IncludeHeaders("X-GitHub-Event")
                    .Route(route => route
                        .When(Condition.Header("X-GitHub-Event").Eq("ping")).Drop()
                        .When(Condition.JsonPath("$.action").In("opened", "closed")).To("rabbit:github.pr")
                        .Otherwise().To("rabbit:github.other")));
        });

        var fromYaml = ConfigurationLoader.LoadFromString("""
            version: 2
            server:
              paths:
                webhooks: /hooks
            brokers:
              rabbit:
                type: rabbitmq
                settings:
                  uri: amqp://localhost
                  exchange_name: webhooks
                publish:
                  retry: { max_retries: 5, delay: 500ms, backoff: linear, factor: 3 }
            webhooks:
              github:
                validate:
                  - type: hmac-sha256
                    header: X-Hub-Signature-256
                    secret: s3cret
                  - type: ip-allowlist
                    allow: [192.30.252.0/22]
                max_body_size: 10MB
                rate_limit: { requests: 10, window: 30s }
                message:
                  headers: [X-GitHub-Event]
                route:
                  - when: { header: X-GitHub-Event, eq: ping }
                    to: []
                  - when: { jsonpath: "$.action", in: [opened, closed] }
                    to: rabbit:github.pr
                  - to: rabbit:github.other
            """);

        fromCode.Server.Paths.Webhooks.ShouldBe(fromYaml.Server.Paths.Webhooks);
        fromCode.Brokers["rabbit"].Publish.Retry.ShouldBeEquivalentTo(fromYaml.Brokers["rabbit"].Publish.Retry);

        var code = fromCode.Webhooks["github"];
        var yaml = fromYaml.Webhooks["github"];
        code.MaxBodySize.ShouldBe(yaml.MaxBodySize);
        code.RateLimit.ShouldBeEquivalentTo(yaml.RateLimit);
        code.Message.HeaderNames.ShouldBe(yaml.Message.HeaderNames);
        // Field by field rather than ShouldBeEquivalentTo: an unset Allow is an empty array here and
        // an empty List from the binder's ternary. Same meaning, different concrete type, and it's
        // the values that have to match across sources.
        code.Validate.Count.ShouldBe(yaml.Validate.Count);
        code.Validate[0].Type.ShouldBe(yaml.Validate[0].Type);
        code.Validate[0].Header.ShouldBe(yaml.Validate[0].Header);
        code.Validate[0].Secret.ShouldBe(yaml.Validate[0].Secret);
        code.Validate[0].Token.ShouldBe(yaml.Validate[0].Token);
        code.Validate[0].Key.ShouldBe(yaml.Validate[0].Key);
        code.Validate[0].Allow.ShouldBe(yaml.Validate[0].Allow);
        code.Validate[1].Type.ShouldBe(yaml.Validate[1].Type);
        code.Validate[1].Allow.ShouldBe(yaml.Validate[1].Allow);

        code.Route.ShouldNotBeNull().Count.ShouldBe(3);
        code.Route[0].To.ShouldBeEmpty();
        code.Route[1].To.ShouldBe(yaml.Route![1].To);
        code.Route[2].When.ShouldBeNull();
        code.Route[2].To.ShouldBe(yaml.Route[2].To);
    }

    [Fact]
    public void AddWebhook_TemplatedTopicAndMetadata_ResolvesPathParameters()
    {
        var configuration = Build(hookpipe => hookpipe
            .AddBroker("console", broker => broker.Stdout())
            .AddBroker("kafka", broker => broker
                .Type("kafka", new KafkaBrokerSettings { BootstrapServers = "localhost:9092" }))
            .AddWebhook("ingest", webhook => webhook
                .Path("ingest/{source}")
                .Metadata("source", "{source}")
                .To("kafka:ingest.{source}", "console")));

        var ingest = configuration.Webhooks["ingest"];
        ingest.Path.ShouldBe("ingest/{source}");
        ingest.Message.Metadata["source"].ShouldBe("{source}");
        ingest.To.ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public void AddWebhook_TargetNamesAnUnknownBroker_FailsTheLoad()
    {
        var error = Should.Throw<HookpipeConfigurationException>(() =>
            Build(hookpipe => hookpipe.AddWebhook("ping", webhook => webhook.To("nope:topic"))));

        error.Message.ShouldContain("unknown broker 'nope'");
    }

    [Fact]
    public void AddWebhook_PlaceholderIsNotAPathParameter_FailsTheLoad()
    {
        var error = Should.Throw<HookpipeConfigurationException>(() =>
            Build(hookpipe => hookpipe
                .AddBroker("console", broker => broker.Stdout())
                .AddWebhook("ping", webhook => webhook
                    .Metadata("who", "{missing}")
                    .To("console"))));

        error.Message.ShouldContain("path parameter 'missing'");
    }

    [Fact]
    public void AddWebhook_TwoWebhooksOnTheSameRoute_FailsTheLoad()
    {
        var error = Should.Throw<HookpipeConfigurationException>(() =>
            Build(hookpipe => hookpipe
                .AddBroker("console", broker => broker.Stdout())
                .AddWebhook("first", webhook => webhook.Path("same").To("console"))
                .AddWebhook("second", webhook => webhook.Path("same").To("console"))));

        error.Message.ShouldContain("is the same as webhook");
    }

    [Fact]
    public void AddBroker_SameIdTwice_FailsTheLoad()
    {
        var error = Should.Throw<HookpipeConfigurationException>(() =>
            Build(hookpipe => hookpipe
                .AddBroker("console", broker => broker.Stdout())
                .AddBroker("CONSOLE", broker => broker.Stdout())
                .AddWebhook("ping", webhook => webhook.To("console"))));

        error.Message.ShouldContain("defined twice");
    }

    [Fact]
    public void AddBroker_SettingsOfTheWrongType_ThrowsImmediately() =>
        Should.Throw<ArgumentException>(() => Build(hookpipe => hookpipe
            .AddBroker("rabbit", broker => broker.Type("rabbitmq", new KafkaBrokerSettings()))))
            .Message.ShouldContain("RabbitMqBrokerSettings");

    [Fact]
    public void AddBroker_UnknownType_ThrowsImmediately() =>
        Should.Throw<ArgumentException>(() => Build(hookpipe => hookpipe
            .AddBroker("x", broker => broker.Type("nope", null))))
            .Message.ShouldContain("Unknown broker type 'nope'");

    [Fact]
    public void AddWebhook_NoTargets_Throws() =>
        Should.Throw<InvalidOperationException>(() => Build(hookpipe => hookpipe
            .AddWebhook("ping", _ => { })))
            .Message.ShouldContain("has no targets");

    [Fact]
    public void AddWebhook_ToAndRouteTogether_Throws() =>
        Should.Throw<InvalidOperationException>(() => Build(hookpipe => hookpipe
            .AddBroker("console", broker => broker.Stdout())
            .AddWebhook("ping", webhook => webhook
                .To("console")
                .Route(route => route.Otherwise().To("console")))))
            .Message.ShouldContain("already uses To or Drop");

    [Fact]
    public void JsonPath_ThatDoesNotParse_ThrowsWhenTheOperatorIsApplied() =>
        Should.Throw<ArgumentException>(() => Condition.JsonPath("$.[").Eq("x"))
            .Message.ShouldContain("invalid JSONPath");

    [Fact]
    public void Metadata_ReferringToOtherMetadata_ThrowsImmediately() =>
        Should.Throw<ArgumentException>(() => new WebhookBuilder("x").Metadata("a", "{metadata.b}"))
            .Message.ShouldContain("can't refer to other metadata");

    [Fact]
    public void LoadYaml_AfterDefiningInCode_Throws() =>
        Should.Throw<InvalidOperationException>(() => Build(hookpipe => hookpipe
            .AddBroker("console", broker => broker.Stdout())
            .AddWebhook("ping", webhook => webhook.To("console"))
            .LoadYaml("whatever.yaml")))
            .Message.ShouldContain("built in code");

    /// <summary>
    /// Runs the configurator the way <c>AddRunaxHookpipe</c> does and returns the built model.
    /// </summary>
    private static HookpipeConfiguration Build(Action<HookpipeConfigurator> configure)
    {
        var services = new ServiceCollection();
        services.AddRunaxHookpipe(configure);

        return services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<HookpipeOptions>()
            .Single()
            .Configuration;
    }
}
