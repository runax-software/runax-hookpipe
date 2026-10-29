using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Kafka;

/// <summary>
/// Broker that produces message envelopes to Kafka topics. The target's topic is the Kafka topic.
/// Records are keyed by webhook ID so each webhook's messages stay in order, and carry
/// <c>hookpipe.message.id</c> and <c>hookpipe.webhook.id</c> headers.
/// </summary>
public sealed class KafkaBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "kafka";

    private readonly ILogger<KafkaBroker> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly string _brokerId;

    private KafkaBroker(ILogger<KafkaBroker> logger, IProducer<string, string> producer, string brokerId)
    {
        _logger = logger;
        _producer = producer;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates an idempotent producer with <c>acks=all</c>.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A configured <see cref="KafkaBroker"/>.</returns>
    public static KafkaBroker Create(string brokerId, KafkaBrokerSettings settings, ILogger<KafkaBroker> logger)
    {
        var configuration = new ProducerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
        };

        var producer = new ProducerBuilder<string, string>(configuration).Build();

        logger.LogInformation("[Hookpipe.Broker:kafka:{BrokerId}] Producing to {BootstrapServers}",
            brokerId, settings.BootstrapServers);

        return new KafkaBroker(logger, producer, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="topic"/> is null or empty.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var kafkaTopic = BrokerHelper.RequireTopic(topic, TypeName, _brokerId);
        var kafkaMessage = new Message<string, string>
        {
            Key = message.WebhookId,
            Value = JsonSerializer.Serialize(message, BrokerHelper.JsonOptions),
            Headers = new Headers
            {
                { "hookpipe.message.id", Encoding.UTF8.GetBytes(message.Id) },
                { "hookpipe.webhook.id", Encoding.UTF8.GetBytes(message.WebhookId) },
            }
        };

        var result = await _producer.ProduceAsync(kafkaTopic, kafkaMessage, cancellationToken);

        _logger.LogDebug(
            "[Hookpipe.Broker:kafka:{BrokerId}] Published message '{MessageId}' to topic='{Topic}' partition={Partition} offset={Offset}",
            _brokerId, message.Id, kafkaTopic, result.Partition.Value, result.Offset.Value);
    }

    /// <inheritdoc />
    public Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var admin = new DependentAdminClientBuilder(_producer.Handle).Build();
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(5));
            return Task.FromResult(new BrokerHealth(BrokerHealthStatus.Healthy, $"{metadata.Brokers.Count} broker(s)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
