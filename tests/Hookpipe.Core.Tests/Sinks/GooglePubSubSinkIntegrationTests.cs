using System.Text.Json;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="GooglePubSubSink"/>.
/// Requires Pub/Sub emulator running on localhost:8085 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class GooglePubSubSinkIntegrationTests : IAsyncLifetime
{
    private const string EmulatorHost = "localhost:8085";
    private const string ProjectId = "test-project";
    private const string ProjectEnv = "TEST_GOOGLE_PROJECT";
    private const string TopicEnv = "TEST_PUBSUB_TOPIC";
    private const string EmulatorEnv = "TEST_PUBSUB_EMULATOR";

    private readonly string _topicId = $"hookpipe-test-{Guid.NewGuid():N}";
    private readonly string _subscriptionId = $"hookpipe-sub-{Guid.NewGuid():N}";

    private GooglePubSubSink _sink = null!;
    private SubscriberClient _subscriber = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", EmulatorHost);
        Environment.SetEnvironmentVariable(ProjectEnv, ProjectId);
        Environment.SetEnvironmentVariable(TopicEnv, _topicId);
        Environment.SetEnvironmentVariable(EmulatorEnv, EmulatorHost);

        // Create topic via admin API
        var publisherApi = await new PublisherServiceApiClientBuilder
        {
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
        var topicName = TopicName.FromProjectTopic(ProjectId, _topicId);
        await publisherApi.CreateTopicAsync(topicName);

        // Create subscription for reading messages
        var subscriberApi = await new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
        var subscriptionName = SubscriptionName.FromProjectSubscription(ProjectId, _subscriptionId);
        await subscriberApi.CreateSubscriptionAsync(subscriptionName, topicName, null, 60);

        // Create sink
        var sinkConfig = new SinkConfig
        {
            Id = "test-pubsub",
            Type = "google-pubsub",
            Settings = new Dictionary<string, string>
            {
                ["project_id_env"] = ProjectEnv,
                ["topic_id_env"] = TopicEnv,
                ["emulator_host_env"] = EmulatorEnv,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = await GooglePubSubSink.CreateAsync(sinkConfig, loggerFactory.CreateLogger<GooglePubSubSink>());

        // Create subscriber for pulling messages
        _subscriber = await new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync();
        await _subscriber.StopAsync(CancellationToken.None);

        Environment.SetEnvironmentVariable(ProjectEnv, null);
        Environment.SetEnvironmentVariable(TopicEnv, null);
        Environment.SetEnvironmentVariable(EmulatorEnv, null);
        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToTopic()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "pubsub-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "pubsub-integration" },
        };

        await _sink.ProduceAsync(envelope);

        PubsubMessage? received = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = _subscriber.StartAsync(async (msg, ct) =>
        {
            received = msg;
            return SubscriberClient.Reply.Ack;
        });

        while (received is null && !cts.Token.IsCancellationRequested)
            await Task.Delay(100, cts.Token);

        received.Should().NotBeNull();
        var json = received!.Data.ToStringUtf8();
        var deserialized = JsonSerializer.Deserialize<JsonElement>(json);
        deserialized.GetProperty("endpointId").GetString().Should().Be("pubsub-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasAttributes()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

        PubsubMessage? received = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = _subscriber.StartAsync(async (msg, ct) =>
        {
            received = msg;
            return SubscriberClient.Reply.Ack;
        });

        while (received is null && !cts.Token.IsCancellationRequested)
            await Task.Delay(100, cts.Token);

        received.Should().NotBeNull();
        received!.Attributes["hookpipe.message.id"].Should().Be(messageId);
        received.Attributes["hookpipe.endpoint.id"].Should().Be("attr-test");
    }
}
