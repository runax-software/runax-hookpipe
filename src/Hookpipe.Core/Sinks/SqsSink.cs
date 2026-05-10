using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that sends message envelopes to an AWS SQS queue.
/// Settings: queue_url_env, region_env (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class SqsSink : ISink, IDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "sqs";

    private readonly ILogger<SqsSink> _logger;
    private readonly IAmazonSQS _client;
    private readonly string _queueUrl;
    private readonly string _sinkId;

    private SqsSink(ILogger<SqsSink> logger, IAmazonSQS client, string queueUrl, string sinkId)
    {
        _logger = logger;
        _client = client;
        _queueUrl = queueUrl;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new SQS sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing queue URL and region settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="SqsSink"/> ready to send messages.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the queue URL env var is not set.
    /// </exception>
    public static SqsSink Create(SinkConfig sinkConfig, ILogger<SqsSink> logger)
    {
        var queueUrl = SinkHelper.RequireEnvVar(sinkConfig, "queue_url_env", "SQS_QUEUE_URL");
        var region = SinkHelper.OptionalEnvVar(sinkConfig, "region_env", "AWS_REGION");
        var serviceUrl = SinkHelper.OptionalEnvVar(sinkConfig, "service_url_env", "AWS_SERVICE_URL");

        var config = new AmazonSQSConfig();
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = region ?? "us-east-1";
        }
        else if (!string.IsNullOrEmpty(region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);

        logger.LogInformation(
            "[Hookpipe.Sink:sqs:{SinkId}] Configured for queue '{QueueUrl}', region='{Region}'",
            sinkConfig.Id, Helpers.LogHelper.MaskUri(queueUrl), region ?? "default");

        return new SqsSink(logger, new AmazonSQSClient(config), queueUrl, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(message, SinkHelper.JsonOptions),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["hookpipe.message.id"] = new()
                {
                    DataType = "String",
                    StringValue = message.Id,
                },
                ["hookpipe.endpoint.id"] = new()
                {
                    DataType = "String",
                    StringValue = message.EndpointId,
                },
            },
        };

        var response = await _client.SendMessageAsync(request, cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Sink:sqs:{SinkId}] Sent message '{MessageId}', SQS message ID='{SqsMessageId}'",
            _sinkId, message.Id, response.MessageId);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
