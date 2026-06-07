using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that publishes message envelopes to an AWS SNS topic.
/// Settings: topic_arn_env, region_env, service_url_env (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class SnsSink : ISink, ISinkHealthCheck, IDisposable
{
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

    public string Type => TypeName;

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

    public void Dispose() => _client.Dispose();
}
