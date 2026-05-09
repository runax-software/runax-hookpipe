using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that produces message envelopes to a Kafka topic.
/// Uses idempotent producer with <see cref="Acks.All"/> for reliable delivery.
/// Settings: brokers_env, topic (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class KafkaSink : ISink, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<KafkaSink> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly string _sinkId;

    private KafkaSink(ILogger<KafkaSink> logger, IProducer<string, string> producer, string topic, string sinkId)
    {
        _logger = logger;
        _producer = producer;
        _topic = topic;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => "kafka";

    /// <summary>
    /// Creates a new Kafka sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing broker and topic settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="KafkaSink"/> ready to produce messages.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the broker env var is not set or the topic setting is missing.
    /// </exception>
    public static KafkaSink Create(SinkConfig sinkConfig, ILogger<KafkaSink> logger)
    {
        var brokersEnv = sinkConfig.Settings.GetValueOrDefault("brokers_env", "KAFKA_BROKERS");
        var brokers = Environment.GetEnvironmentVariable(brokersEnv)
                      ?? throw new InvalidOperationException(
                          $"Sink '{sinkConfig.Id}': env var '{brokersEnv}' is not set");

        var topic = sinkConfig.Settings.GetValueOrDefault("topic", "")
            is { Length: > 0 } t
            ? t
            : throw new InvalidOperationException($"Sink '{sinkConfig.Id}': 'topic' setting is required");

        var config = new ProducerConfig
        {
            BootstrapServers = brokers,
            Acks = Acks.All,
            EnableIdempotence = true,
        };

        var producer = new ProducerBuilder<string, string>(config).Build();

        logger.LogInformation("[Hookpipe.Sink:kafka:{SinkId}] Connected to {Brokers}, topic='{Topic}'",
            sinkConfig.Id, brokers, topic);

        return new KafkaSink(logger, producer, topic, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);

        var kafkaMessage = new Message<string, string>
        {
            Key = message.EndpointId,
            Value = json,
            Headers = new Headers
            {
                { "hookpipe.message.id", System.Text.Encoding.UTF8.GetBytes(message.Id) },
                { "hookpipe.endpoint.id", System.Text.Encoding.UTF8.GetBytes(message.EndpointId) },
            }
        };

        var result = await _producer.ProduceAsync(_topic, kafkaMessage, cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Sink:kafka:{SinkId}] Published message '{MessageId}' to topic='{Topic}' partition={Partition} offset={Offset}",
            _sinkId, message.Id, _topic, result.Partition.Value, result.Offset.Value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
