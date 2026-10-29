using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Azure.ServiceBus;

/// <summary>
/// Broker that sends message envelopes to Azure Service Bus. The target's topic is the queue or
/// topic name. Senders for the topics the configuration uses are created up front, and any other name
/// gets one on first use. Messages carry
/// <c>hookpipe.message.id</c> and <c>hookpipe.webhook.id</c> application properties.
/// </summary>
public sealed class ServiceBusBroker : IBroker, IBrokerHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "azure-servicebus";

    private readonly ILogger<ServiceBusBroker> _logger;
    private readonly ServiceBusClient _client;
    private readonly string _brokerId;
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new();

    private ServiceBusBroker(ILogger<ServiceBusBroker> logger, ServiceBusClient client, string brokerId)
    {
        _logger = logger;
        _client = client;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the Service Bus client.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <param name="topics">Queue and topic names the configuration publishes to, probed by the health check.</param>
    /// <returns>A configured <see cref="ServiceBusBroker"/>.</returns>
    public static ServiceBusBroker Create(
        string brokerId,
        ServiceBusBrokerSettings settings,
        ILogger<ServiceBusBroker> logger,
        IReadOnlyCollection<string>? topics = null)
    {
        var client = new ServiceBusClient(settings.ConnectionString);
        var broker = new ServiceBusBroker(logger, client, brokerId);
        foreach (var topic in topics ?? []) broker.GetSender(topic);

        logger.LogInformation("[Hookpipe.Broker:azure-servicebus:{BrokerId}] Connected to '{Namespace}'",
            brokerId, client.FullyQualifiedNamespace);

        return broker;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var queueOrTopic = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var sender = GetSender(queueOrTopic);

        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message, BrokerHelper.JsonOptions))
        {
            ContentType = "application/json",
            MessageId = message.Id,
            ApplicationProperties =
            {
                ["hookpipe.message.id"] = message.Id,
                ["hookpipe.webhook.id"] = message.WebhookId,
            }
        };

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
        _logger.LogDebug("[Hookpipe.Broker:azure-servicebus:{BrokerId}] Sent message '{MessageId}' to '{QueueOrTopic}'",
            _brokerId, message.Id, queueOrTopic);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Opens (or reuses) each sender's link by creating an empty batch, which works for queues and
    /// topics and needs only Send rights. Reports <see cref="BrokerHealthStatus.Unknown"/> while no
    /// target uses this broker.
    /// </remarks>
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsClosed) return new BrokerHealth(BrokerHealthStatus.Unhealthy, "client is closed");
        if (_senders.IsEmpty) return new BrokerHealth(BrokerHealthStatus.Unknown, "no queue or topic to probe");

        try
        {
            foreach (var sender in _senders.Values)
            {
                using var batch = await sender.Value.CreateMessageBatchAsync(cancellationToken);
            }

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
        _logger.LogDebug("[Hookpipe.Broker:azure-servicebus:{BrokerId}] Closing connection", _brokerId);

        foreach (var sender in _senders.Values.Where(sender => sender.IsValueCreated))
            await sender.Value.CloseAsync();

        await _client.DisposeAsync();
    }

    private ServiceBusSender GetSender(string queueOrTopic) =>
        _senders.GetOrAdd(queueOrTopic, name => new Lazy<ServiceBusSender>(() => _client.CreateSender(name))).Value;
}
