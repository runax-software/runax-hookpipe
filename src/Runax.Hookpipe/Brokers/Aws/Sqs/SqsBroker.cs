using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Aws.Sqs;

/// <summary>
/// Broker that sends message envelopes to SQS queues. The target's topic picks the queue through
/// <see cref="SqsBrokerSettings.TopicQueueUrlMap"/>. Messages carry <c>hookpipe.message.id</c> and
/// <c>hookpipe.webhook.id</c> attributes.
/// </summary>
public sealed class SqsBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "sqs";

    private readonly ILogger<SqsBroker> _logger;
    private readonly IAmazonSQS _client;
    private readonly IReadOnlyDictionary<string, string> _queueUrls;
    private readonly string _brokerId;

    private SqsBroker(
        ILogger<SqsBroker> logger,
        IAmazonSQS client,
        IReadOnlyDictionary<string, string> queueUrls,
        string brokerId)
    {
        _logger = logger;
        _client = client;
        _queueUrls = queueUrls;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the SQS client.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A configured <see cref="SqsBroker"/>.</returns>
    public static SqsBroker Create(string brokerId, SqsBrokerSettings settings, ILogger<SqsBroker> logger)
    {
        var configuration = new AmazonSQSConfig();
        AwsClients.Configure(configuration, settings.Region, settings.ServiceUrl);

        var credentials = AwsClients.EndpointCredentials(settings.ServiceUrl);
        var client = credentials is null ? new AmazonSQSClient(configuration) : new AmazonSQSClient(credentials, configuration);

        logger.LogInformation(
            "[Hookpipe.Broker:sqs:{BrokerId}] Configured for {Count} queue(s), region='{Region}'",
            brokerId, settings.TopicQueueUrlMap.Count, settings.Region ?? "default");

        return new SqsBroker(logger, client, settings.TopicQueueUrlMap, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the topic has no queue URL.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var queueTopic = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        if (!_queueUrls.TryGetValue(queueTopic, out var queueUrl))
            throw new ArgumentException($"Broker '{_brokerId}' has no queue URL for topic '{queueTopic}'", nameof(topic));

        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = JsonSerializer.Serialize(message, BrokerHelper.JsonOptions),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["hookpipe.message.id"] = new() { DataType = "String", StringValue = message.Id },
                ["hookpipe.webhook.id"] = new() { DataType = "String", StringValue = message.WebhookId },
            },
        };

        var response = await _client.SendMessageAsync(request, cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Broker:sqs:{BrokerId}] Sent message '{MessageId}' to topic '{Topic}', SQS message ID='{SqsMessageId}'",
            _brokerId, message.Id, queueTopic, response.MessageId);
    }

    /// <inheritdoc />
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var queueUrl in _queueUrls.Values)
            {
                await _client.GetQueueAttributesAsync(
                    new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn"] },
                    cancellationToken);
            }

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
