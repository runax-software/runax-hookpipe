using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that sends message envelopes to an Azure Service Bus queue or topic.
/// Settings: connection_string_env, queue_or_topic (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class ServiceBusSink : ISink, IAsyncDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "servicebus";

    private readonly ILogger<ServiceBusSink> _logger;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusClient _client;
    private readonly string _sinkId;

    private ServiceBusSink(
        ILogger<ServiceBusSink> logger,
        ServiceBusClient client,
        ServiceBusSender sender,
        string sinkId)
    {
        _logger = logger;
        _client = client;
        _sender = sender;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new Service Bus sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing connection string and queue/topic settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="ServiceBusSink"/> ready to send messages.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string env var is not set or the queue_or_topic setting is missing.
    /// </exception>
    public static ServiceBusSink Create(SinkConfig sinkConfig, ILogger<ServiceBusSink> logger)
    {
        var connectionString = SinkHelper.RequireEnvVar(sinkConfig, "connection_string_env",
            "SERVICEBUS_CONNECTION_STRING");

        var queueOrTopic = sinkConfig.Settings.GetValueOrDefault("queue_or_topic", "")
            is { Length: > 0 } name
            ? name
            : throw new InvalidOperationException($"Sink '{sinkConfig.Id}': 'queue_or_topic' setting is required");

        var client = new ServiceBusClient(connectionString);
        var sender = client.CreateSender(queueOrTopic);

        logger.LogInformation("[Hookpipe.Sink:servicebus:{SinkId}] Connected to '{QueueOrTopic}'", sinkConfig.Id,
            queueOrTopic);

        return new ServiceBusSink(logger, client, sender, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(message, SinkHelper.JsonOptions))
        {
            ContentType = "application/json",
            MessageId = message.Id,
            ApplicationProperties =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.endpoint.id"] = message.EndpointId,
            }
        };

        await _sender.SendMessageAsync(sbMessage, cancellationToken);
        _logger.LogDebug("[Hookpipe.Sink:servicebus:{SinkId}] Sent message '{MessageId}'", _sinkId, message.Id);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Sink:servicebus:{SinkId}] Closing connection", _sinkId);
        await _sender.CloseAsync();
        await _client.DisposeAsync();
    }
}
