using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Aws.EventBridge;

/// <summary>
/// Broker that puts message envelopes on an Amazon EventBridge bus. The target's topic becomes the
/// event's <c>DetailType</c>; the envelope JSON is the event's <c>Detail</c>.
/// </summary>
public sealed class EventBridgeBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "eventbridge";

    private readonly ILogger<EventBridgeBroker> _logger;
    private readonly IAmazonEventBridge _client;
    private readonly string _eventBus;
    private readonly string _source;
    private readonly string _brokerId;

    private EventBridgeBroker(
        ILogger<EventBridgeBroker> logger,
        IAmazonEventBridge client,
        string eventBus,
        string source,
        string brokerId)
    {
        _logger = logger;
        _client = client;
        _eventBus = eventBus;
        _source = source;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the EventBridge client.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A configured <see cref="EventBridgeBroker"/>.</returns>
    public static EventBridgeBroker Create(
        string brokerId,
        EventBridgeBrokerSettings settings,
        ILogger<EventBridgeBroker> logger)
    {
        var configuration = new AmazonEventBridgeConfig();
        AwsClients.Configure(configuration, settings.Region, settings.ServiceUrl);

        var credentials = AwsClients.EndpointCredentials(settings.ServiceUrl);
        var client = credentials is null
            ? new AmazonEventBridgeClient(configuration)
            : new AmazonEventBridgeClient(credentials, configuration);

        logger.LogInformation(
            "[Hookpipe.Broker:eventbridge:{BrokerId}] Configured for bus '{EventBus}', source='{Source}'",
            brokerId, settings.EventBusName, settings.Source);

        return new EventBridgeBroker(logger, client, settings.EventBusName, settings.Source, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when EventBridge rejects the event.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var detailType = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = _eventBus,
                    Source = _source,
                    DetailType = detailType,
                    Detail = JsonSerializer.Serialize(message, BrokerHelper.JsonOptions),
                }
            ]
        };

        var response = await _client.PutEventsAsync(request, cancellationToken);

        if (response.FailedEntryCount > 0)
        {
            _logger.LogWarning(
                "[Hookpipe.Broker:eventbridge:{BrokerId}] Failed to put event '{MessageId}': {Error}",
                _brokerId, message.Id, response.Entries[0].ErrorMessage);
            throw new InvalidOperationException(
                $"EventBridge put failed: {response.Entries[0].ErrorCode} - {response.Entries[0].ErrorMessage}");
        }

        _logger.LogDebug(
            "[Hookpipe.Broker:eventbridge:{BrokerId}] Put event '{MessageId}' as '{DetailType}', EventId='{EventId}'",
            _brokerId, message.Id, detailType, response.Entries[0].EventId);
    }

    /// <inheritdoc />
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DescribeEventBusAsync(new DescribeEventBusRequest { Name = _eventBus }, cancellationToken);
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
