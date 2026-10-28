using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Runax.Hookpipe.Config;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Sinks;

/// <summary>
/// Sink that publishes message envelopes to an AWS SNS topic.
/// Settings: topic_arn_env, region_env, service_url_env (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class SnsSink : ISink, ISinkHealthCheck, IDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "sns";

    private readonly ILogger<SnsSink> _logger;
    private readonly IAmazonSimpleNotificationService _client;
    private readonly string _topicArn;
    private readonly string _sinkId;

    private SnsSink(ILogger<SnsSink> logger, IAmazonSimpleNotificationService client, string topicArn, string sinkId)
    {
        _logger = logger;
        _client = client;
        _topicArn = topicArn;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new SNS sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing topic ARN and region settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="SnsSink"/> ready to publish messages.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the topic ARN env var is not set.
    /// </exception>
    public static SnsSink Create(SinkConfig sinkConfig, ILogger<SnsSink> logger)
    {
        var topicArn = SinkHelper.RequireEnvVar(sinkConfig, "topic_arn_env", "SNS_TOPIC_ARN");
        var region = SinkHelper.OptionalEnvVar(sinkConfig, "region_env", "AWS_REGION");
        var serviceUrl = SinkHelper.OptionalEnvVar(sinkConfig, "service_url_env", "AWS_SERVICE_URL");

        var config = new AmazonSimpleNotificationServiceConfig();
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = region ?? "us-east-1";
        }
        else if (!string.IsNullOrEmpty(region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);

        logger.LogInformation("[Hookpipe.Sink:sns:{SinkId}] Configured for topic '{TopicArn}'", sinkConfig.Id,
            topicArn);

        var client = !string.IsNullOrEmpty(serviceUrl)
            ? new AmazonSimpleNotificationServiceClient(
                new Amazon.Runtime.BasicAWSCredentials(
                    Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "test",
                    Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "test"),
                config)
            : new AmazonSimpleNotificationServiceClient(config);

        return new SnsSink(logger, client, topicArn, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var request = new PublishRequest
        {
            TopicArn = _topicArn,
            Message = JsonSerializer.Serialize(message, SinkHelper.JsonOptions),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["hookpipe.message.id"] = new() { DataType = "String", StringValue = message.Id },
                ["hookpipe.endpoint.id"] = new() { DataType = "String", StringValue = message.EndpointId },
            },
        };

        var response = await _client.PublishAsync(request, cancellationToken);

        _logger.LogDebug("[Hookpipe.Sink:sns:{SinkId}] Published message '{MessageId}', SNS ID='{SnsMessageId}'",
            _sinkId, message.Id, response.MessageId);
    }

    /// <inheritdoc />
    public async Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetTopicAttributesAsync(_topicArn, cancellationToken);
            return new SinkHealth(SinkHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
