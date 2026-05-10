using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "sqs";

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
        var queueUrlEnv = sinkConfig.Settings.GetValueOrDefault("queue_url_env", "SQS_QUEUE_URL");
        var queueUrl = Environment.GetEnvironmentVariable(queueUrlEnv)
                       ?? throw new InvalidOperationException(
                           $"Sink '{sinkConfig.Id}': env var '{queueUrlEnv}' is not set");

        var regionEnv = sinkConfig.Settings.GetValueOrDefault("region_env", "AWS_REGION");
        var region = Environment.GetEnvironmentVariable(regionEnv);

        var config = new AmazonSQSConfig();
        if (!string.IsNullOrEmpty(region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);

        logger.LogInformation(
            "[Hookpipe.Sink:sqs:{SinkId}] Configured for queue '{QueueUrl}', region='{Region}'",
            sinkConfig.Id, Helpers.LogHelper.MaskUri(queueUrl), region ?? "default");

        return new SqsSink(logger, new AmazonSQSClient(config), queueUrl, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);

        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = json,
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
