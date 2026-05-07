using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that produces message envelope to a Kafka topic.
/// Settings: brokers_env, topic (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class KafkaSink : ISink, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<KafkaSink> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    private KafkaSink(ILogger<KafkaSink> logger, IProducer<string, string> producer, string topic)
    {
        _logger = logger;
        _producer = producer;
        _topic = topic;
    }

    /// <inheritdoc />
    public string Type => "kafka";

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

        logger.LogInformation("Kafka sink '{SinkId}' connected to {Brokers}, topic='{Topic}'", sinkConfig.Id, brokers,
            topic);

        return new KafkaSink(logger, producer, topic);
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
            "Produced message '{MessageId}' to topic='{Topic}' partition={Partition} offset={Offset}",
            message.Id, _topic, result.Partition.Value, result.Offset.Value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
