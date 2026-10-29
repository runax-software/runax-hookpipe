using System.Text.Json;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Runax.Hookpipe.Brokers.Redis;

/// <summary>
/// Broker that appends message envelopes to Redis streams. The target's topic is the stream key.
/// Each entry holds <c>hookpipe.message.id</c>, <c>hookpipe.webhook.id</c>, and the envelope JSON in
/// <c>payload</c>.
/// </summary>
public sealed class RedisStreamBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "redis";

    private readonly ILogger<RedisStreamBroker> _logger;
    private readonly IConnectionMultiplexer _connection;
    private readonly string _brokerId;

    private RedisStreamBroker(ILogger<RedisStreamBroker> logger, IConnectionMultiplexer connection, string brokerId)
    {
        _logger = logger;
        _connection = connection;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Connects to Redis.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A connected <see cref="RedisStreamBroker"/>.</returns>
    public static RedisStreamBroker Create(string brokerId, RedisBrokerSettings settings, ILogger<RedisStreamBroker> logger)
    {
        var connection = ConnectionMultiplexer.Connect(settings.Configuration);

        logger.LogInformation("[Hookpipe.Broker:redis:{BrokerId}] Connected", brokerId);

        return new RedisStreamBroker(logger, connection, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var streamKey = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var entryId = await _connection.GetDatabase().StreamAddAsync(streamKey,
        [
            new NameValueEntry("hookpipe.message.id", message.Id),
            new NameValueEntry("hookpipe.webhook.id", message.WebhookId),
            new NameValueEntry("payload", JsonSerializer.Serialize(message, BrokerHelper.JsonOptions)),
        ]);

        _logger.LogDebug(
            "[Hookpipe.Broker:redis:{BrokerId}] Appended message '{MessageId}' to stream '{StreamKey}', entry={EntryId}",
            _brokerId, message.Id, streamKey, entryId);
    }

    /// <inheritdoc />
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.GetDatabase().PingAsync();
            return new BrokerHealth(BrokerHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
