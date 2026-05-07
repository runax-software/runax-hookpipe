using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that publishes messages envelopes to a RabbitMq exchange.
/// Settings: url_env, exchange, routing_key (all from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class RabbitMqSink : ISink, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<RabbitMqSink> _logger;
    private readonly string _exchange;
    private readonly string _routingKey;
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqSink(
        ILogger<RabbitMqSink> logger,
        IConnection connection,
        IChannel channel,
        string exchange,
        string routingKey)
    {
        _logger = logger;
        _connection = connection;
        _channel = channel;
        _exchange = exchange;
        _routingKey = routingKey;
    }

    /// <inheritdoc />
    public string Type => "rabbitmq";

    /// <summary>
    /// Creates a new RabbitMQ sink from the given config settings.
    /// </summary>
    public static async Task<RabbitMqSink> CreateAsync(SinkConfig sinkConfig, ILogger<RabbitMqSink> logger)
    {
        var urlEnv = sinkConfig.Settings.GetValueOrDefault("url_env", "RABBITMQ_URL");
        var url = Environment.GetEnvironmentVariable(urlEnv)
                  ?? throw new InvalidOperationException(
                      $"Sink '{sinkConfig.Id}': env var '{urlEnv}' is not set");

        var exchange = sinkConfig.Settings.GetValueOrDefault("exchange", "");
        var routingKey = sinkConfig.Settings.GetValueOrDefault("routing_key", "");

        var factory = new ConnectionFactory { Uri = new Uri(url) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        if (!string.IsNullOrEmpty(exchange))
            await channel.ExchangeDeclareAsync(
                exchange: exchange,
                type: ExchangeType.Topic,
                durable: true);

        logger.LogInformation(
            "RabbitMQ sink '{SinkId}' connected to {Url}, exchange='{Exchange}', routing_key='{RoutingKey}'",
            sinkConfig.Id, url, exchange, routingKey);

        return new RabbitMqSink(logger, connection, channel, exchange, routingKey);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
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

        _logger.LogDebug("Published message '{MessageId}' to exchange='{Exchange}' routing_key='{RoutingKey}'",
            message.Id, _exchange, _routingKey);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
    }
}
