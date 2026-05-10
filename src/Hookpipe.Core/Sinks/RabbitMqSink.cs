using System.Text;
using System.Text.Json;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that publishes message envelopes to a RabbitMQ exchange as persistent JSON messages.
/// Declares the exchange as topic/durable on startup.
/// Settings: url_env, exchange, routing_key (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class RabbitMqSink : ISink, IAsyncDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "rabbitmq";

    private readonly ILogger<RabbitMqSink> _logger;
    private readonly string _exchange;
    private readonly string _routingKey;
    private readonly string _sinkId;
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqSink(
        ILogger<RabbitMqSink> logger,
        IConnection connection,
        IChannel channel,
        string exchange,
        string routingKey,
        string sinkId)
    {
        _logger = logger;
        _connection = connection;
        _channel = channel;
        _exchange = exchange;
        _routingKey = routingKey;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new RabbitMQ sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing connection and exchange settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="RabbitMqSink"/> connected and ready to publish.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URL env var is not set.</exception>
    public static async Task<RabbitMqSink> CreateAsync(SinkConfig sinkConfig, ILogger<RabbitMqSink> logger)
    {
        var url = SinkHelper.RequireEnvVar(sinkConfig, "url_env", "RABBITMQ_URL");
        var exchange = sinkConfig.Settings.GetValueOrDefault("exchange", "");
        var routingKey = sinkConfig.Settings.GetValueOrDefault("routing_key", "");

        var factory = new ConnectionFactory { Uri = new Uri(url) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        if (!string.IsNullOrEmpty(exchange))
        {
            await channel.ExchangeDeclareAsync(
                exchange: exchange,
                type: ExchangeType.Topic,
                durable: true);

            logger.LogDebug("[Hookpipe.Sink:rabbitmq:{SinkId}] Declared exchange '{Exchange}' (topic, durable)",
                sinkConfig.Id, exchange);
        }

        logger.LogInformation(
            "[Hookpipe.Sink:rabbitmq:{SinkId}] Connected to {Url}, exchange='{Exchange}', routing_key='{RoutingKey}'",
            sinkConfig.Id, Helpers.LogHelper.MaskUri(url), exchange, routingKey);

        return new RabbitMqSink(logger, connection, channel, exchange, routingKey, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, SinkHelper.JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id,
        };

        await _channel.BasicPublishAsync(
            exchange: _exchange,
            routingKey: _routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Sink:rabbitmq:{SinkId}] Published message '{MessageId}' to exchange='{Exchange}' routing_key='{RoutingKey}'",
            _sinkId, message.Id, _exchange, _routingKey);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("[Hookpipe.Sink:rabbitmq:{SinkId}] Closing connection", _sinkId);
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
