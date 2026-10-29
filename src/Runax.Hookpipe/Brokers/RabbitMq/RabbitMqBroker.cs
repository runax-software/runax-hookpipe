using System.Text;
using System.Text.Json;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Runax.Hookpipe.Brokers.RabbitMq;

/// <summary>
/// Broker that publishes message envelopes to a RabbitMQ exchange as persistent JSON messages. The
/// target's topic is the routing key. Declares the exchange as topic and durable on startup.
/// </summary>
public sealed class RabbitMqBroker : IBroker, IBrokerHealthCheck, IAsyncDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "rabbitmq";

    private readonly ILogger<RabbitMqBroker> _logger;
    private readonly string _exchange;
    private readonly string _brokerId;
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqBroker(
        ILogger<RabbitMqBroker> logger,
        IConnection connection,
        IChannel channel,
        string exchange,
        string brokerId)
    {
        _logger = logger;
        _connection = connection;
        _channel = channel;
        _exchange = exchange;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Connects to RabbitMQ and declares the exchange.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A connected <see cref="RabbitMqBroker"/>.</returns>
    public static async Task<RabbitMqBroker> CreateAsync(
        string brokerId,
        RabbitMqBrokerSettings settings,
        ILogger<RabbitMqBroker> logger)
    {
        var factory = new ConnectionFactory { Uri = new Uri(settings.Uri) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: settings.ExchangeName, type: ExchangeType.Topic, durable: true);

        logger.LogInformation(
            "[Hookpipe.Broker:rabbitmq:{BrokerId}] Connected to {Url}, exchange='{Exchange}' (topic, durable)",
            brokerId, Helpers.LogHelper.MaskUri(settings.Uri), settings.ExchangeName);

        return new RabbitMqBroker(logger, connection, channel, settings.ExchangeName, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var routingKey = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, BrokerHelper.JsonOptions));

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id,
        };

        await _channel.BasicPublishAsync(
            exchange: _exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Broker:rabbitmq:{BrokerId}] Published message '{MessageId}' to exchange='{Exchange}' routing_key='{RoutingKey}'",
            _brokerId, message.Id, _exchange, routingKey);
    }

    /// <inheritdoc />
    public Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = _connection.IsOpen && _channel.IsOpen
            ? new BrokerHealth(BrokerHealthStatus.Healthy)
            : new BrokerHealth(BrokerHealthStatus.Unhealthy, "connection or channel is closed");

        return Task.FromResult(health);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Broker:rabbitmq:{BrokerId}] Closing connection", _brokerId);
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
