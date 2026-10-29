using System.Collections.Concurrent;
using System.Text.Json;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Google.PubSub;

/// <summary>
/// Broker that publishes message envelopes to Google Cloud Pub/Sub. The target's topic is the topic
/// ID in <see cref="GooglePubSubBrokerSettings.ProjectId"/>; one publisher per topic is created on
/// first use. The health check probes every topic the configuration publishes to. Uses the emulator when <c>PUBSUB_EMULATOR_HOST</c> is set. Messages carry
/// <c>hookpipe.message.id</c> and <c>hookpipe.webhook.id</c> attributes.
/// </summary>
public sealed class GooglePubSubBroker : IBroker, IBrokerHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "google-pubsub";

    private readonly ILogger<GooglePubSubBroker> _logger;
    private readonly string _projectId;
    private readonly string _brokerId;
    private readonly ConcurrentDictionary<string, Lazy<Task<PublisherClient>>> _publishers = new();
    private readonly IReadOnlyCollection<string> _configuredTopics;

    private GooglePubSubBroker(
        ILogger<GooglePubSubBroker> logger,
        string projectId,
        string brokerId,
        IReadOnlyCollection<string> configuredTopics)
    {
        _logger = logger;
        _projectId = projectId;
        _brokerId = brokerId;
        _configuredTopics = configuredTopics;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the broker. Publishers connect lazily, one per topic.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <param name="topics">Topic IDs the configuration publishes to, probed by the health check.</param>
    /// <returns>A configured <see cref="GooglePubSubBroker"/>.</returns>
    public static GooglePubSubBroker Create(
        string brokerId,
        GooglePubSubBrokerSettings settings,
        ILogger<GooglePubSubBroker> logger,
        IReadOnlyCollection<string>? topics = null)
    {
        logger.LogInformation("[Hookpipe.Broker:google-pubsub:{BrokerId}] Configured for project '{ProjectId}'",
            brokerId, settings.ProjectId);

        return new GooglePubSubBroker(logger, settings.ProjectId, brokerId, topics ?? []);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var topicId = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var publisher = await GetPublisherAsync(topicId);

        var pubsubMessage = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(JsonSerializer.Serialize(message, BrokerHelper.JsonOptions)),
            Attributes =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.webhook.id"] = message.WebhookId,
            },
        };

        var messageId = await publisher.PublishAsync(pubsubMessage);

        _logger.LogDebug(
            "[Hookpipe.Broker:google-pubsub:{BrokerId}] Published message '{MessageId}' to topic '{Topic}', Pub/Sub ID='{PubSubMessageId}'",
            _brokerId, message.Id, topicId, messageId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads every topic the configuration publishes to, plus any published to since. Reports
    /// <see cref="BrokerHealthStatus.Unknown"/> while no target uses this broker.
    /// </remarks>
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var topicIds = _configuredTopics.Union(_publishers.Keys).ToList();
        if (topicIds.Count == 0) return new BrokerHealth(BrokerHealthStatus.Unknown, "no topic to probe");

        try
        {
            var api = await new PublisherServiceApiClientBuilder
            {
                EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            }.BuildAsync(cancellationToken);

            foreach (var topicId in topicIds)
                await api.GetTopicAsync(TopicName.FromProjectTopic(_projectId, topicId), cancellationToken);

            return new BrokerHealth(BrokerHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Broker:google-pubsub:{BrokerId}] Shutting down publishers", _brokerId);

        foreach (var publisher in _publishers.Values.Where(publisher => publisher.IsValueCreated))
        {
            if (publisher.Value.IsCompletedSuccessfully)
                await publisher.Value.Result.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
    }

    private Task<PublisherClient> GetPublisherAsync(string topicId)
    {
        var publisher = _publishers.GetOrAdd(topicId, id => new Lazy<Task<PublisherClient>>(() =>
            new PublisherClientBuilder
            {
                TopicName = TopicName.FromProjectTopic(_projectId, id),
                EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            }.BuildAsync()));

        if (publisher.Value.IsFaulted) _publishers.TryRemove(new(topicId, publisher));
        return publisher.Value;
    }
}
