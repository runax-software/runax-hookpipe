using System.Text.Json;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Google.Api.Gax;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that publishes message envelopes to a Google Cloud Pub/Sub topic.
/// Settings: project_id_env, topic_id_env, emulator_host_env (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class GooglePubSubSink : ISink, ISinkHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "google-pubsub";

    private readonly ILogger<GooglePubSubSink> _logger;
    private readonly PublisherClient _publisher;
    private readonly TopicName _topicName;
    private readonly string _sinkId;

    private GooglePubSubSink(
        ILogger<GooglePubSubSink> logger, PublisherClient publisher, TopicName topicName, string sinkId)
    {
        _logger = logger;
        _publisher = publisher;
        _topicName = topicName;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    public static async Task<GooglePubSubSink> CreateAsync(SinkConfig sinkConfig, ILogger<GooglePubSubSink> logger)
    {
        var projectId = SinkHelper.RequireEnvVar(sinkConfig, "project_id_env", "GOOGLE_CLOUD_PROJECT");
        var topicId = SinkHelper.RequireEnvVar(sinkConfig, "topic_id_env", "PUBSUB_TOPIC_ID");
        var emulatorHost = SinkHelper.OptionalEnvVar(sinkConfig, "emulator_host_env", "PUBSUB_EMULATOR_HOST");

        if (!string.IsNullOrEmpty(emulatorHost))
            Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", emulatorHost);

        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        var publisherBuilder = new PublisherClientBuilder
        {
            TopicName = topicName,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
        };
        var publisher = await publisherBuilder.BuildAsync();

        logger.LogInformation(
            "[Hookpipe.Sink:google-pubsub:{SinkId}] Connected to project='{ProjectId}', topic='{TopicId}'",
            sinkConfig.Id, projectId, topicId);

        return new GooglePubSubSink(logger, publisher, topicName, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var pubsubMessage = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(JsonSerializer.Serialize(message, SinkHelper.JsonOptions)),
            Attributes =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.endpoint.id"] = message.EndpointId,
            },
        };

        var messageId = await _publisher.PublishAsync(pubsubMessage);

        _logger.LogDebug(
            "[Hookpipe.Sink:google-pubsub:{SinkId}] Published message '{MessageId}', Pub/Sub ID = '{PubSubMessageId}'",
            _sinkId, message.Id, messageId);
    }

    /// <inheritdoc />
    public async Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var api = await new PublisherServiceApiClientBuilder
            {
                EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
            }.BuildAsync(cancellationToken);

            await api.GetTopicAsync(_topicName, cancellationToken);
            return new SinkHealth(SinkHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Sink:google-pubsub:{SinkId}] Shutting down publisher", _sinkId);
        await _publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
    }
}
