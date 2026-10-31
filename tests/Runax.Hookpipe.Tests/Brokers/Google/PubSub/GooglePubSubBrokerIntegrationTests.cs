using System.Text.Json;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Google.PubSub;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Google.PubSub;

/// <summary>
/// Integration tests for <see cref="GooglePubSubBroker"/>.
/// Requires Pub/Sub emulator running on localhost:8085 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class GooglePubSubBrokerIntegrationTests : IAsyncLifetime
{
    private const string EmulatorHost = "localhost:8085";
    private const string ProjectId = "test-project";

    private readonly string _topicId = $"hookpipe-test-{Guid.NewGuid():N}";
    private readonly string _subscriptionId = $"hookpipe-sub-{Guid.NewGuid():N}";

    private GooglePubSubBroker _broker = null!;
    private SubscriberClient _subscriber = null!;

    public async ValueTask InitializeAsync()
    {
        // The broker and the admin clients find the emulator through this variable
        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", EmulatorHost);

        // Create topic via admin API
        var publisherApi = await new PublisherServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
        var topicName = TopicName.FromProjectTopic(ProjectId, _topicId);
        await publisherApi.CreateTopicAsync(topicName);

        // Create subscription for reading messages
        var subscriberApi = await new SubscriberServiceApiClientBuilder
        {
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
        var subscriptionName = SubscriptionName.FromProjectSubscription(ProjectId, _subscriptionId);
        await subscriberApi.CreateSubscriptionAsync(subscriptionName, topicName, null, 60);

        // Create broker
        var settings = new GooglePubSubBrokerSettings { ProjectId = ProjectId };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = GooglePubSubBroker.Create("test-pubsub", settings, loggerFactory.CreateLogger<GooglePubSubBroker>());

        // Create subscriber for pulling messages
        _subscriber = await new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
        }.BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _subscriber.StopAsync(CancellationToken.None);

        Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToTopic()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "pubsub-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "pubsub-integration" },
        };

        await _broker.PublishAsync(_topicId, envelope);

        PubsubMessage? received = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = _subscriber.StartAsync((msg, _) =>
        {
            received = msg;
            return Task.FromResult(SubscriberClient.Reply.Ack);
        });

        while (received is null && !cts.Token.IsCancellationRequested)
            await Task.Delay(100, cts.Token);

        received.ShouldNotBeNull();
        var json = received!.Data.ToStringUtf8();
        var deserialized = JsonSerializer.Deserialize<JsonElement>(json);
        deserialized.GetProperty("webhookId").GetString().ShouldBe("pubsub-integration-test");
        deserialized.GetProperty("method").GetString().ShouldBe("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasAttributes()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(_topicId, envelope);

        PubsubMessage? received = null;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = _subscriber.StartAsync((msg, _) =>
        {
            received = msg;
            return Task.FromResult(SubscriberClient.Reply.Ack);
        });

        while (received is null && !cts.Token.IsCancellationRequested)
            await Task.Delay(100, cts.Token);

        received.ShouldNotBeNull();
        received!.Attributes["hookpipe.message.id"].ShouldBe(messageId);
        received.Attributes["hookpipe.webhook.id"].ShouldBe("attr-test");
    }
}
