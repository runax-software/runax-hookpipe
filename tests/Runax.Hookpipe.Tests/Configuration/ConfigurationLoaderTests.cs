using FluentAssertions;
using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Brokers.RabbitMq;

namespace Runax.Hookpipe.Tests.Configuration;

public sealed class ConfigurationLoaderTests
{
    private const string Console = """
        brokers:
          console:
            type: stdout
        """;

    [Fact]
    public void LoadFromString_FullConfiguration_BindsBrokersAndWebhooks()
    {
        var secretEnv = $"HOOKPIPE_TEST_SECRET_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(secretEnv, "s3cret");

        var configuration = ConfigurationLoader.LoadFromString($$"""
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
                  retry: { max_retries: 5, delay: 500ms, backoff: linear, factor: 3, max_delay: 1m, jitter: false }
              console:
                type: stdout
            webhooks:
              github:
                validate:
                  - type: hmac-sha256
                    header: X-Hub-Signature-256
                    secret: ${{{secretEnv}}}
                  - type: ip-allowlist
                    allow: [192.30.252.0/22, 10.0.0.1]
                message:
                  headers: [X-GitHub-Event]
                route:
                  - when: { header: X-GitHub-Event, eq: ping }
                    to: []
                  - when: { jsonpath: "$.action", in: [opened, closed] }
                    to: rabbit:github.pr
                  - to: [rabbit:github.other, console]
              ingest:
                path: /ingest/{source}
                methods: [post, put]
                rate_limit: { requests: 10, window: 30s }
                message:
                  body: false
                  metadata: { source: "{source}" }
                to: console
            """);

        configuration.Server.Paths.Webhooks.Should().Be("/hooks");

        var rabbit = configuration.Brokers["rabbit"];
        rabbit.Type.Should().Be("rabbitmq");
        rabbit.Settings.Should().BeOfType<RabbitMqBrokerSettings>()
            .Which.Should().BeEquivalentTo(new RabbitMqBrokerSettings { Uri = "amqp://localhost", ExchangeName = "webhooks" });
        rabbit.Publish.Retry.Should().BeEquivalentTo(new RetryConfiguration
        {
            MaxRetries = 5,
            Delay = TimeSpan.FromMilliseconds(500),
            Backoff = RetryBackoff.Linear,
            Factor = 3,
            MaxDelay = TimeSpan.FromMinutes(1),
            Jitter = false,
        });
        configuration.Brokers["console"].Settings.Should().BeNull();

        var github = configuration.Webhooks["github"];
        github.Path.Should().Be("github");
        github.Methods.Should().Equal("POST");
        github.Validate.Should().HaveCount(2);
        github.Validate[0].Should().BeEquivalentTo(
            new ValidatorConfiguration { Type = "hmac-sha256", Header = "X-Hub-Signature-256", Secret = "s3cret" });
        github.Validate[1].Allow.Should().Equal("192.30.252.0/22", "10.0.0.1");
        github.Message.IncludeHeaders.Should().BeTrue();
        github.Message.HeaderNames.Should().Equal("X-GitHub-Event");
        github.To.Should().BeNull();
        github.Route.Should().HaveCount(3);
        github.Route![0].To.Should().BeEmpty();
        github.Route[0].When.Should().BeOfType<CompareCondition>()
            .Which.Should().Match<CompareCondition>(condition =>
                condition.Source == ConditionSource.Header && condition.Operator == ConditionOperator.Eq);
        github.Route[1].To.Should().Equal(new Target("rabbit", "github.pr"));
        github.Route[2].When.Should().BeNull();
        github.Route[2].To.Should().Equal(new Target("rabbit", "github.other"), new Target("console", null));

        var ingest = configuration.Webhooks["ingest"];
        ingest.Path.Should().Be("ingest/{source}");
        ingest.Methods.Should().Equal("POST", "PUT");
        ingest.RateLimit.Should().BeEquivalentTo(new RateLimitConfiguration { Requests = 10, Window = TimeSpan.FromSeconds(30) });
        ingest.Message.Body.Should().BeFalse();
        ingest.Message.Metadata.Should().Equal(new Dictionary<string, string> { ["source"] = "{source}" });
        ingest.To.Should().Equal(new Target("console", null));
    }

    [Fact]
    public void LoadFromString_MinimalWebhook_UsesDefaults()
    {
        var configuration = Load($$"""
            {{Console}}
            webhooks:
              ping:
                to: console
            """);

        var ping = configuration.Webhooks["ping"];
        configuration.Server.Paths.Webhooks.Should().Be("/webhooks");
        ping.Path.Should().Be("ping");
        ping.Methods.Should().Equal("POST");
        ping.Validate.Should().BeEmpty();
        ping.RateLimit.Should().BeNull();
        ping.Message.Body.Should().BeTrue();
        ping.Message.IncludeHeaders.Should().BeFalse();
        configuration.Brokers["console"].Publish.Retry.Should().BeNull();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/hooks/")]
    public void LoadFromString_WebhookPrefix_IsNormalized(string prefix)
    {
        var configuration = Load($$"""
            server:
              paths:
                webhooks: {{prefix}}
            {{Console}}
            """);

        configuration.Server.Paths.Webhooks.Should().Be(prefix == "/" ? "/" : "/api/hooks");
    }

    [Fact]
    public void LoadFromString_KeysInOtherCases_MatchIgnoringCaseAndUnderscores()
    {
        var configuration = Load("""
            Brokers:
              rabbit:
                Type: rabbitmq
                Settings:
                  Uri: amqp://localhost
                  ExchangeName: events
                Publish:
                  Retry: { MaxRetries: 1, maxDelay: 5s }
            """);

        configuration.Brokers["rabbit"].Settings.Should().BeOfType<RabbitMqBrokerSettings>()
            .Which.ExchangeName.Should().Be("events");
        configuration.Brokers["rabbit"].Publish.Retry!.MaxRetries.Should().Be(1);
        configuration.Brokers["rabbit"].Publish.Retry!.MaxDelay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LoadFromString_IdsIgnoreCase_ResolvesTargetsToTheDefinedId()
    {
        var configuration = Load("""
            brokers:
              Console:
                type: stdout
            webhooks:
              ping:
                to: console
            """);

        configuration.Brokers.Should().ContainKey("console");
        configuration.Webhooks["PING"].To.Should().Equal(new Target("Console", null));
    }

    [Fact]
    public void LoadFromString_NoVersion_RejectsTheFileAsV1()
    {
        var errors = Errors("""
            endpoints:
              - id: test
                path: /test
                broker: stdout
            brokers:
              - id: stdout
                type: stdout
            """);

        errors.Should().ContainSingle()
            .Which.Should().Contain("version: missing").And.Contain("v1").And.Contain("docs/upgrading.md");
    }

    [Fact]
    public void LoadFromString_OtherVersion_Throws()
    {
        var errors = Errors("""
            version: 3
            """);

        errors.Should().ContainSingle().Which.Should().StartWith("version: unsupported version");
    }

    [Fact]
    public void LoadFromString_UnknownKey_ReportsItsPath()
    {
        var errors = Errors("""
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              github:
                validate:
                  - type: hmac-sha256
                    header: X-Hub-Signature-256
                    secret: abc
                    secrett: abc
                to: console
            """);

        errors.Should().Equal("webhooks.github.validate[0].secrett: unknown key");
    }

    [Fact]
    public void LoadFromString_SeveralProblems_ReportsEveryOne()
    {
        var act = () => ConfigurationLoader.LoadFromString("""
            version: 2
            brokers:
              console:
                type: stdout
                colour: blue
            webhooks:
              a:
                to: missing
              b:
                route:
                  - to: console
                  - to: console
            typo: true
            """, "test.yaml");

        var exception = act.Should().Throw<HookpipeConfigurationException>().Which;
        exception.ConfigurationSource.Should().Be("test.yaml");
        exception.Message.Should().Contain("test.yaml").And.Contain("4 errors");
        exception.Errors.Should().BeEquivalentTo(
            "brokers.console.colour: unknown key",
            "webhooks.a.to: target 'missing' names unknown broker 'missing'",
            "webhooks.b.route[0]: only the last rule may leave out 'when'; the rules after it could never match",
            "typo: unknown key");
    }

    [Fact]
    public void LoadFromString_SameKeyInTwoSpellings_IsADuplicate()
    {
        var errors = Errors("""
            brokers:
              rabbit:
                type: rabbitmq
                settings:
                  uri: amqp://localhost
                  exchange_name: a
                  ExchangeName: b
            """);

        errors.Should().Equal("brokers.rabbit.settings.ExchangeName: duplicate key (same as 'exchange_name')");
    }

    [Fact]
    public void LoadFromString_DuplicateIdIgnoringCase_Throws()
    {
        var errors = Errors("""
            brokers:
              console:
                type: stdout
              Console:
                type: stdout
            """);

        errors.Should().ContainSingle().Which.Should().StartWith("brokers.Console: duplicate ID");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("slash/id")]
    public void LoadFromString_InvalidId_Throws(string id)
    {
        var errors = Errors($$"""
            brokers:
              "{{id}}":
                type: stdout
            """);

        errors.Should().ContainSingle().Which.Should().Contain("IDs may use only letters, digits");
    }

    [Fact]
    public void LoadFromString_BrokerNamedNone_Throws()
    {
        Errors("""
            brokers:
              none:
                type: stdout
            """).Should().Contain(error => error.Contains("'none' is reserved"));
    }

    [Fact]
    public void LoadFromString_Interpolation_ResolvesVariablesDefaultsFilesAndEscapes()
    {
        var urlEnv = $"HOOKPIPE_TEST_URL_{Guid.NewGuid():N}";
        var unsetEnv = $"HOOKPIPE_TEST_UNSET_{Guid.NewGuid():N}";
        var secretFile = Path.GetTempFileName();
        File.WriteAllText(secretFile, "file-secret\n");
        Environment.SetEnvironmentVariable(urlEnv, "amqp://env-host");

        try
        {
            var configuration = Load($$"""
                brokers:
                  rabbit:
                    type: rabbitmq
                    settings:
                      uri: ${{{urlEnv}}}
                      exchange_name: ${{{unsetEnv}}:-fallback}
                webhooks:
                  a:
                    validate:
                      - type: bearer
                        token: ${file:{{secretFile}}}
                    message:
                      metadata: { literal: "$${NOT_A_VARIABLE}" }
                    to: rabbit:events
                """);

            configuration.Brokers["rabbit"].Settings.Should().BeEquivalentTo(
                new RabbitMqBrokerSettings { Uri = "amqp://env-host", ExchangeName = "fallback" });
            configuration.Webhooks["a"].Validate[0].Token.Should().Be("file-secret");
            configuration.Webhooks["a"].Message.Metadata["literal"].Should().Be("${NOT_A_VARIABLE}");
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Fact]
    public void LoadFromString_MissingVariable_NamesTheVariable()
    {
        var missingEnv = $"HOOKPIPE_TEST_MISSING_{Guid.NewGuid():N}";

        var errors = Errors($$"""
            brokers:
              rabbit:
                type: rabbitmq
                settings:
                  uri: ${{{missingEnv}}}
            """);

        errors.Should().Contain($"brokers.rabbit.settings.uri: environment variable '{missingEnv}' is not set");
    }

    [Fact]
    public void LoadFromString_InterpolatedValues_NeverAppearInErrors()
    {
        var typeEnv = $"HOOKPIPE_TEST_TYPE_{Guid.NewGuid():N}";
        var delayEnv = $"HOOKPIPE_TEST_DELAY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(typeEnv, "top-secret-type");
        Environment.SetEnvironmentVariable(delayEnv, "top-secret-delay");

        var act = () => Load($$"""
            brokers:
              a:
                type: ${{{typeEnv}}}
              b:
                type: stdout
                publish:
                  retry: { delay: "${{{delayEnv}}}" }
            """);

        var exception = act.Should().Throw<HookpipeConfigurationException>().Which;
        exception.Errors.Should().HaveCount(2);
        exception.Message.Should().NotContain("top-secret");
    }

    [Fact]
    public void LoadFromString_UnknownBrokerType_ListsKnownTypes()
    {
        Errors("""
            brokers:
              queue:
                type: kafak
            """).Should().ContainSingle().Which.Should().Contain("unknown broker type 'kafak'").And.Contain("kafka");
    }

    [Fact]
    public void LoadFromString_BrokerSettings_AreStrictAndRequired()
    {
        var errors = Errors("""
            brokers:
              rabbit:
                type: rabbitmq
                settings:
                  url_env: RABBITMQ_URL
              kafka:
                type: kafka
              console:
                type: stdout
                settings:
                  pretty: true
            """);

        errors.Should().BeEquivalentTo(
            "brokers.rabbit.settings.url_env: unknown key",
            "brokers.rabbit.settings.uri: required",
            "brokers.kafka.settings.bootstrap_servers: required",
            "brokers.console.settings: 'stdout' brokers take no settings");
    }

    [Theory]
    [InlineData("delay: soon", "brokers.console.publish.retry.delay: expected a duration")]
    [InlineData("backoff: random", "brokers.console.publish.retry.backoff: expected one of: constant, linear, exponential")]
    [InlineData("max_retries: -1", "brokers.console.publish.retry.max_retries: must be at least 0")]
    [InlineData("factor: 0.5", "brokers.console.publish.retry.factor: must be at least 1")]
    public void LoadFromString_InvalidRetry_Throws(string setting, string expected)
    {
        Errors($$"""
            brokers:
              console:
                type: stdout
                publish:
                  retry:
                    {{setting}}
            """).Should().ContainSingle().Which.Should().StartWith(expected);
    }

    [Fact]
    public void LoadFromString_WebhookWithoutTargets_Throws()
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                methods: [POST]
            """).Should().Equal("webhooks.a: needs 'to' or 'route' (use 'to: none' to publish nothing)");
    }

    [Fact]
    public void LoadFromString_WebhookWithToAndRoute_Throws()
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                to: console
                route:
                  - to: console
            """).Should().Equal("webhooks.a: has both 'to' and 'route'; use one");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("[]")]
    public void LoadFromString_NoTargets_MeansPublishNothing(string to)
    {
        Load($$"""
            webhooks:
              a:
                to: {{to}}
            """).Webhooks["a"].To.Should().BeEmpty();
    }

    [Theory]
    [InlineData("kafka", "target 'kafka' needs a topic")]
    [InlineData("console:events", "'stdout' brokers take no topic")]
    [InlineData("\"kafka:\"", "has an empty topic")]
    [InlineData("\":events\"", "names no broker")]
    [InlineData("\"kafka:ingest.{source}\"", "path parameter 'source' isn't in the webhook's path 'a'")]
    [InlineData("\"kafka:ingest.{topic}\"", "can't be used in a topic")]
    [InlineData("\"kafka:a/{webhook_id}\"", "'a/' can't be part of a templated topic")]
    [InlineData("\"kafka:x.{metadata.tenant}\"", "metadata key 'tenant' isn't in the webhook's message.metadata")]
    [InlineData("\"kafka:x.{bad name}\"", "isn't valid")]
    [InlineData("\"kafka:x.{$.a b}\"", "invalid JSONPath")]
    [InlineData("\"kafka:x.{$.type:-a/b}\"", "the default in '{$.type:-a/b}'")]
    [InlineData("\"kafka:a{{b}}\"", "can't contain '{' or '}'")]
    [InlineData("[console, none]", "'none' can't be combined with other targets")]
    [InlineData("{ broker: kafka, topic: payments, partition_key: \"{webhook_id}\" }", "prerequisite P2")]
    [InlineData("sqs:unmapped", "topic 'unmapped' isn't in brokers.sqs.settings.topic_queue_url_map")]
    public void LoadFromString_InvalidTarget_Throws(string to, string expected)
    {
        Errors($$"""
            brokers:
              console:
                type: stdout
              kafka:
                type: kafka
                settings:
                  bootstrap_servers: localhost:9092
              sqs:
                type: sqs
                settings:
                  topic_queue_url_map:
                    orders: http://localhost:4566/000000000000/orders
            webhooks:
              a:
                to: {{to}}
            """).Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public void LoadFromString_TemplatedTopics_ParseTheirTemplates()
    {
        var webhook = Load("""
            brokers:
              kafka:
                type: kafka
                settings:
                  bootstrap_servers: localhost:9092
              sqs:
                type: sqs
                settings:
                  topic_queue_url_map:
                    orders: http://localhost:4566/000000000000/orders
            webhooks:
              ingest:
                path: ingest/{source}
                message:
                  metadata:
                    tenant: "{header.X-Tenant:-none}"
                to:
                  - "kafka:payments.{$.type:-other}"
                  - "kafka:{metadata.tenant}.{source}"
                  - "sqs:{$.queue}"
                  - kafka:static
            """).Webhooks["ingest"];

        webhook.To!.Select(target => target.TopicTemplate?.Text)
            .Should().Equal("payments.{$.type:-other}", "{metadata.tenant}.{source}", "{$.queue}", null);
        webhook.To![0].TopicTemplate!.Placeholders.Single().Default.Should().Be("other");
        webhook.Message.MetadataTemplates["tenant"].Placeholders.Single().Source.Should().Be(TemplateSource.Header);
    }

    [Theory]
    [InlineData("\"{topic}\"", "placeholder '{topic}' can't be used in metadata values")]
    [InlineData("\"{metadata.other}\"", "metadata values can't refer to other metadata")]
    [InlineData("\"{tenant}\"", "path parameter 'tenant' isn't in the webhook's path 'ingest/{source}'")]
    [InlineData("\"{source\"", "has no closing '}'")]
    public void LoadFromString_InvalidMetadataTemplate_Throws(string value, string expected)
    {
        Errors($$"""
            brokers:
              console:
                type: stdout
            webhooks:
              ingest:
                path: ingest/{source}
                message:
                  metadata:
                    other: x
                    value: {{value}}
                to: console
            """).Should().ContainSingle().Which.Should()
            .StartWith("webhooks.ingest.message.metadata.value:").And.Contain(expected);
    }

    [Fact]
    public void LoadFromString_RouteTargetTemplate_ChecksPathParameters()
    {
        Errors("""
            brokers:
              kafka:
                type: kafka
                settings:
                  bootstrap_servers: localhost:9092
            webhooks:
              ingest:
                route:
                  - when: { header: X-Event, eq: "{tenant}" }
                    to: "kafka:events.{source}"
                  - to: kafka:other
            """).Should().BeEquivalentTo(
            "webhooks.ingest.route[0].when: path parameter 'tenant' isn't in the webhook's path 'ingest'",
            "webhooks.ingest.route[0].to: placeholder '{source}': path parameter 'source' isn't in the webhook's path 'ingest'");
    }

    [Fact]
    public void LoadFromString_ConditionValueWithOtherPlaceholder_Throws()
    {
        Errors("""
            brokers:
              console:
                type: stdout
            webhooks:
              a:
                route:
                  - when: { header: X-Event, eq: "{header.X-Other}" }
                    to: console
                  - to: none
            """).Should().ContainSingle().Which.Should().Contain("condition values can only use path parameters");
    }

    [Fact]
    public void LoadFromString_TargetObject_BindsBrokerAndTopic()
    {
        Load("""
            brokers:
              sqs:
                type: sqs
                settings:
                  topic_queue_url_map:
                    orders: http://localhost:4566/000000000000/orders
            webhooks:
              a:
                to: { broker: sqs, topic: orders }
            """).Webhooks["a"].To.Should().Equal(new Target("sqs", "orders"));
    }

    [Fact]
    public void LoadFromString_LastRouteRuleWithCondition_Throws()
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                route:
                  - when: { header: X-Event, eq: push }
                    to: console
            """).Should().Equal(
            "webhooks.a.route[0].when: the last rule can't have 'when'; add a final rule with only 'to'");
    }

    [Theory]
    [InlineData("- type: shopify", "unknown validator type 'shopify'")]
    [InlineData("- { type: hmac-sha256, header: X-Signature }", "webhooks.a.validate[0].secret: required by 'hmac-sha256'")]
    [InlineData("- { type: bearer, token: abc, header: X-Token }", "webhooks.a.validate[0].header: unknown key")]
    [InlineData("- { token: abc }", "webhooks.a.validate[0].type: required")]
    [InlineData("- { type: bearer, token: \"\" }", "webhooks.a.validate[0].token: is empty")]
    [InlineData("- { type: ip-allowlist, allow: [] }", "webhooks.a.validate[0].allow: is empty")]
    [InlineData("- { type: ip-allowlist, allow: [10.0.0.0/-1] }", "'10.0.0.0/-1' isn't an IP address or CIDR range")]
    [InlineData("- { type: ip-allowlist, allow: [10.0.0.0/40] }", "'10.0.0.0/40' isn't an IP address or CIDR range")]
    [InlineData("- { type: ip-allowlist, allow: [not-an-ip] }", "'not-an-ip' isn't an IP address or CIDR range")]
    public void LoadFromString_InvalidValidator_Throws(string validator, string expected)
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                validate:
                  {{validator}}
                to: console
            """).Should().ContainSingle().Which.Should().Contain(expected);
    }

    [Fact]
    public void LoadFromString_IpAllowlistAsCommaSeparatedText_SplitsIt()
    {
        var allowEnv = $"HOOKPIPE_TEST_ALLOW_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(allowEnv, "10.0.0.1, 192.168.0.0/16");

        Load($$"""
            {{Console}}
            webhooks:
              a:
                validate:
                  - type: ip-allowlist
                    allow: ${{{allowEnv}}}
                to: console
            """).Webhooks["a"].Validate[0].Allow.Should().Equal("10.0.0.1", "192.168.0.0/16");
    }

    [Fact]
    public void LoadFromString_StripeWithoutHeader_IsAccepted()
    {
        Load($$"""
            {{Console}}
            webhooks:
              stripe:
                validate:
                  - type: stripe-v1
                    secret: whsec_test
                to: console
            """).Webhooks["stripe"].Validate[0].Header.Should().BeNull();
    }

    [Fact]
    public void LoadFromString_SameRouteTwice_Throws()
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                path: ingest/{source}
                to: console
              b:
                path: /Ingest/{name}
                to: console
            """).Should().ContainSingle().Which.Should().Contain("is the same as webhook 'a's route");
    }

    [Theory]
    [InlineData("/", "health/live")]
    [InlineData("/", "metrics")]
    [InlineData("/health", "brokers")]
    public void LoadFromString_RouteShadowingServiceRoutes_Throws(string prefix, string path)
    {
        Errors($$"""
            server:
              paths:
                webhooks: {{prefix}}
            {{Console}}
            webhooks:
              a:
                path: {{path}}
                to: console
            """).Should().ContainSingle().Which.Should().Contain("would shadow the service's /health or /metrics routes");
    }

    [Theory]
    [InlineData("ingest/{topic}", "uses a reserved name")]
    [InlineData("ingest/{*rest}", "must be a plain name")]
    [InlineData("ingest/{id:int}", "must be a plain name")]
    [InlineData("ingest/", "can't end with '/'")]
    public void LoadFromString_InvalidWebhookPath_Throws(string path, string expected)
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                path: {{path}}
                to: console
            """).Should().Contain(error => error.Contains(expected));
    }

    [Fact]
    public void LoadFromString_InvalidMethod_Throws()
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                methods: [POST, "GET /"]
                to: console
            """).Should().Equal("webhooks.a.methods: 'GET /' isn't an HTTP method");
    }

    [Fact]
    public void LoadFromString_CombinedConditions_BindsTheTree()
    {
        var when = Load($$"""
            {{Console}}
            webhooks:
              a:
                route:
                  - when:
                      any:
                        - - { jsonpath: "$.type", starts_with: "charge." }
                          - { jsonpath: "$.amount", gte: 100000 }
                        - not: { header: X-Priority, in: [low, normal] }
                    to: console
                  - to: none
            """).Webhooks["a"].Route![0].When;

        var any = when.Should().BeOfType<AnyCondition>().Which;
        any.Conditions.Should().HaveCount(2);
        var all = any.Conditions[0].Should().BeOfType<AllCondition>().Which;
        all.Conditions[1].Should().BeOfType<CompareCondition>()
            .Which.Operands.Should().Equal(new ConditionValue("100000", ConditionValueKind.Number));
        any.Conditions[1].Should().BeOfType<NotCondition>()
            .Which.Condition.Should().BeOfType<CompareCondition>()
            .Which.Operands.Should().Equal(ConditionValue.Of("low"), ConditionValue.Of("normal"));
    }

    [Theory]
    [InlineData("{ jsonpath: \"$.a[\", eq: x }", "webhooks.a.route[0].when.jsonpath: invalid JSONPath")]
    [InlineData("{ jsonpath: event.type, eq: x }", "must start with '$'")]
    [InlineData("{ header: X-A, regex: \"([a-z\" }", "webhooks.a.route[0].when.regex: invalid regex")]
    [InlineData("{ jsonpath: $.amount, gt: \"100\" }", "'gt' needs a number")]
    [InlineData("{ jsonpath: $.status, in: paid }", "expected a list of values")]
    [InlineData("{ jsonpath: $.status, exists: yes }", "'exists' needs true or false")]
    [InlineData("{ jsonpath: $.status, eq: a, ne: b }", "has more than one operator")]
    [InlineData("{ eq: paid }", "needs one of 'jsonpath', 'header', or 'path'")]
    [InlineData("{ header: X-A, path: b, eq: c }", "has more than one source")]
    [InlineData("{ header: X-A, eq: b, flag: c }", "webhooks.a.route[0].when.flag: unknown key")]
    [InlineData("{ all: [], any: [] }", "must be the only key")]
    [InlineData("{ all: [] }", "needs at least one condition")]
    [InlineData("{ path: tenant, eq: acme }", "path parameter 'tenant' isn't in the webhook's path 'a'")]
    public void LoadFromString_InvalidCondition_Throws(string when, string expected)
    {
        Errors($$"""
            {{Console}}
            webhooks:
              a:
                route:
                  - when: {{when}}
                    to: console
                  - to: none
            """).Should().Contain(error => error.Contains(expected));
    }

    [Fact]
    public void LoadFromString_ConditionsNestedTooDeep_Throws()
    {
        var when = string.Concat(Enumerable.Repeat("{ not: ", 8)) + "{ header: X-A, eq: b }" +
                   string.Concat(Enumerable.Repeat(" }", 8));

        Errors($$"""
            {{Console}}
            webhooks:
              a:
                route:
                  - when: {{when}}
                    to: console
                  - to: none
            """).Should().ContainSingle().Which.Should().Contain("conditions nest deeper than 8 levels");
    }

    [Fact]
    public void LoadFromString_InvalidYaml_ReportsTheLine()
    {
        Errors("""
            version: 2
            brokers: [unclosed
            """).Should().ContainSingle().Which.Should().Contain("invalid YAML at line");
    }

    [Fact]
    public void LoadFromString_EmptyDocument_Throws()
    {
        Errors(string.Empty).Should().Equal("(root): the config is empty");
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFound()
    {
        var act = () => ConfigurationLoader.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.yaml"));

        act.Should().Throw<FileNotFoundException>();
    }

    private static HookpipeConfiguration Load(string body) => ConfigurationLoader.LoadFromString($"version: 2\n{body}");

    private static IReadOnlyList<string> Errors(string yaml)
    {
        var source = yaml.StartsWith("version:", StringComparison.Ordinal) || yaml.Length == 0 ||
                     yaml.Contains("endpoints:")
            ? yaml
            : $"version: 2\n{yaml}";
        var act = () => ConfigurationLoader.LoadFromString(source);

        return act.Should().Throw<HookpipeConfigurationException>().Which.Errors;
    }
}
