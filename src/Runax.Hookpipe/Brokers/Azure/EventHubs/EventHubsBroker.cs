using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Azure.EventHubs;

/// <summary>
/// Broker that sends message envelopes to Azure Event Hubs. The target's topic is the event hub
/// name. Producers for the hubs the configuration uses are created up front, and any other hub gets one on
/// first use. Events carry <c>hookpipe.message.id</c> and
/// <c>hookpipe.webhook.id</c> properties.
/// </summary>
public sealed class EventHubsBroker : IBroker, IBrokerHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "azure-eventhubs";

    private readonly ILogger<EventHubsBroker> _logger;
    private readonly string _connectionString;
    private readonly string _brokerId;
    private readonly ConcurrentDictionary<string, Lazy<EventHubProducerClient>> _producers = new();

    private EventHubsBroker(ILogger<EventHubsBroker> logger, string connectionString, string brokerId)
    {
        _logger = logger;
        _connectionString = connectionString;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the broker, with a producer per event hub the configuration publishes to.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <param name="topics">Event hub names the configuration publishes to, probed by the health check.</param>
    /// <returns>A configured <see cref="EventHubsBroker"/>.</returns>
    public static EventHubsBroker Create(
        string brokerId,
        EventHubsBrokerSettings settings,
        ILogger<EventHubsBroker> logger,
        IReadOnlyCollection<string>? topics = null)
    {
        var broker = new EventHubsBroker(logger, settings.ConnectionString, brokerId);
        foreach (var topic in topics ?? []) broker.GetProducer(topic);

        logger.LogInformation("[Hookpipe.Broker:azure-eventhubs:{BrokerId}] Configured", brokerId);

        return broker;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var eventHubName = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var producer = GetProducer(eventHubName);

        var eventData = new EventData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, BrokerHelper.JsonOptions)))
        {
            ContentType = "application/json",
            MessageId = message.Id,
            Properties =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.webhook.id"] = message.WebhookId,
            }
        };

        await producer.SendAsync([eventData], cancellationToken);
        _logger.LogDebug("[Hookpipe.Broker:azure-eventhubs:{BrokerId}] Sent message '{MessageId}' to '{EventHub}'",
            _brokerId, message.Id, eventHubName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads each event hub's properties. Reports <see cref="BrokerHealthStatus.Unknown"/> while no
    /// target uses this broker.
    /// </remarks>
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_producers.IsEmpty) return new BrokerHealth(BrokerHealthStatus.Unknown, "no event hub to probe");

        try
        {
            foreach (var producer in _producers.Values)
                await producer.Value.GetEventHubPropertiesAsync(cancellationToken);

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
        _logger.LogDebug("[Hookpipe.Broker:azure-eventhubs:{BrokerId}] Closing connections", _brokerId);

        foreach (var producer in _producers.Values.Where(producer => producer.IsValueCreated))
            await producer.Value.DisposeAsync();
    }

    private EventHubProducerClient GetProducer(string eventHubName) =>
        _producers.GetOrAdd(eventHubName, name =>
            new Lazy<EventHubProducerClient>(() => new EventHubProducerClient(_connectionString, name))).Value;
}
