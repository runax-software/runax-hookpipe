using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Aws.Sns;

/// <summary>
/// Broker that publishes message envelopes to SNS topics. The target's topic picks the topic ARN
/// through <see cref="SnsBrokerSettings.TopicArnMap"/>. Messages carry <c>hookpipe.message.id</c>
/// and <c>hookpipe.webhook.id</c> attributes.
/// </summary>
public sealed class SnsBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "sns";

    private readonly ILogger<SnsBroker> _logger;
    private readonly IAmazonSimpleNotificationService _client;
    private readonly IReadOnlyDictionary<string, string> _topicArns;
    private readonly string _brokerId;

    private SnsBroker(
        ILogger<SnsBroker> logger,
        IAmazonSimpleNotificationService client,
        IReadOnlyDictionary<string, string> topicArns,
        string brokerId)
    {
        _logger = logger;
        _client = client;
        _topicArns = topicArns;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the SNS client.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A configured <see cref="SnsBroker"/>.</returns>
    public static SnsBroker Create(string brokerId, SnsBrokerSettings settings, ILogger<SnsBroker> logger)
    {
        var configuration = new AmazonSimpleNotificationServiceConfig();
        AwsClients.Configure(configuration, settings.Region, settings.ServiceUrl);

        var credentials = AwsClients.EndpointCredentials(settings.ServiceUrl);
        var client = credentials is null
            ? new AmazonSimpleNotificationServiceClient(configuration)
            : new AmazonSimpleNotificationServiceClient(credentials, configuration);

        logger.LogInformation("[Hookpipe.Broker:sns:{BrokerId}] Configured for {Count} topic(s), region='{Region}'",
            brokerId, settings.TopicArnMap.Count, settings.Region ?? "default");

        return new SnsBroker(logger, client, settings.TopicArnMap, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the topic has no ARN.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var snsTopic = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        if (!_topicArns.TryGetValue(snsTopic, out var topicArn))
            throw new ArgumentException($"Broker '{_brokerId}' has no topic ARN for topic '{snsTopic}'", nameof(topic));

        var request = new PublishRequest
        {
            TopicArn = topicArn,
            Message = JsonSerializer.Serialize(message, BrokerHelper.JsonOptions),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["hookpipe.message.id"] = new() { DataType = "String", StringValue = message.Id },
                ["hookpipe.webhook.id"] = new() { DataType = "String", StringValue = message.WebhookId },
            },
        };

        var response = await _client.PublishAsync(request, cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Broker:sns:{BrokerId}] Published message '{MessageId}' to topic '{Topic}', SNS ID='{SnsMessageId}'",
            _brokerId, message.Id, snsTopic, response.MessageId);
    }

    /// <inheritdoc />
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var topicArn in _topicArns.Values)
                await _client.GetTopicAttributesAsync(topicArn, cancellationToken);

            return new BrokerHealth(BrokerHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
