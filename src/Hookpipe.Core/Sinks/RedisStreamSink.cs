using System.Text.Json;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that appends message envelope to a Redis Stream.
/// Settings: connection_env, stream_key (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class RedisStreamSink : ISink, IDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "redis-stream";

    private readonly ILogger<RedisStreamSink> _logger;
    private readonly IConnectionMultiplexer _connection;
    private readonly string _streamKey;
    private readonly string _sinkId;

    private RedisStreamSink(
        ILogger<RedisStreamSink> logger,
        IConnectionMultiplexer connection,
        string streamKey,
        string sinkId)
    {
        _logger = logger;
        _connection = connection;
        _streamKey = streamKey;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new Redis Stream sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing connection and stream settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="RedisStreamSink"/> ready to append messages.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection env var is not set or the stream key is missing.
    /// </exception>
    public static RedisStreamSink Create(SinkConfig sinkConfig, ILogger<RedisStreamSink> logger)
    {
        var connectionString = SinkHelper.RequireEnvVar(sinkConfig, "connection_env", "REDIS_CONNECTION");
        var streamKey = sinkConfig.Settings.GetValueOrDefault("stream_key", "")
            is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException(
                $"Sink '{sinkConfig.Id}': 'stream_key' setting is required");

        var connection = ConnectionMultiplexer.Connect(connectionString);

        logger.LogInformation(
            "[Hookpipe.Sink:redis-stream:{SinkId}] Connected, stream='{StreamKey}'",
            sinkConfig.Id, streamKey);

        return new RedisStreamSink(logger, connection, streamKey, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var entryId = await _connection.GetDatabase().StreamAddAsync(_streamKey,
        [
            new NameValueEntry("hookpipe.message.id", message.Id),
            new NameValueEntry("hookpipe.endpoint.id", message.EndpointId),
            new NameValueEntry("payload", JsonSerializer.Serialize(message, SinkHelper.JsonOptions)),
        ]);

        _logger.LogDebug(
            "[Hookpipe.Sink:redis-stream:{SinkId}] Appended message '{MessageId}' to stream '{StreamKey}', entry={EntryId}",
            _sinkId, message.Id, _streamKey, entryId);
    }

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
