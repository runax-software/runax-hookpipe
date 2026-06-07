using System.Text;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that sends message envelopes to an Azure Event Hub.
/// Settings: connection_string_env, event_hub_name (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class EventHubSink : ISink, ISinkHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "eventhub";

    private readonly ILogger<EventHubSink> _logger;
    private readonly EventHubProducerClient _producer;
    private readonly string _sinkId;

    private EventHubSink(ILogger<EventHubSink> logger, EventHubProducerClient producer, string sinkId)
    {
        _logger = logger;
        _producer = producer;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new Event Hub sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing connection string and event hub name.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="EventHubSink"/> ready to send events.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string env var is not set or the event_hub_name setting is missing.
    /// </exception>
    public static EventHubSink Create(SinkConfig sinkConfig, ILogger<EventHubSink> logger)
    {
        var connectionString = SinkHelper.RequireEnvVar(sinkConfig, "connection_string_env",
            "EVENTHUB_CONNECTION_STRING");

        var eventHubName = sinkConfig.Settings.GetValueOrDefault("event_hub_name", "")
            is { Length: > 0 } name
            ? name
            : throw new InvalidOperationException($"Sink '{sinkConfig.Id}': 'event_hub_name' setting is required");

        var producer = new EventHubProducerClient(connectionString, eventHubName);

        logger.LogInformation("[Hookpipe.Sink:eventhub:{SinkId}] Connected to event hub '{EventHubName}'",
            sinkConfig.Id, eventHubName);

        return new EventHubSink(logger, producer, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var eventData = new EventData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, SinkHelper.JsonOptions)))
        {
            ContentType = "application/json",
            MessageId = message.Id,
            Properties =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.endpoint.id"] = message.EndpointId,
            }
        };

        await _producer.SendAsync([eventData], cancellationToken);
        _logger.LogDebug("[Hookpipe.Sink:eventhub:{SinkId}] Sent message '{MessageId}'", _sinkId, message.Id);
    }

    /// <inheritdoc />
    public async Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _producer.GetEventHubPropertiesAsync(cancellationToken);
            return new SinkHealth(SinkHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Sink:eventhub:{SinkId}] Closing connection", _sinkId);
        await _producer.DisposeAsync();
    }
}
