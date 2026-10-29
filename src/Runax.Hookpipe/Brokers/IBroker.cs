using Runax.Hookpipe.Models;

namespace Runax.Hookpipe.Brokers;

/// <summary>
/// Publishes message envelopes to one broker (SQS, Kafka, RabbitMQ, and so on). One instance
/// serves every target on its broker; the target's topic comes with each message.
/// </summary>
public interface IBroker
{
    /// <summary>
    /// Broker type identifier (e.g. "sqs", "kafka", "stdout"). Matches <see cref="Configuration.BrokerConfiguration.Type"/>.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Publishes a message envelope to a topic on this broker.
    /// </summary>
    /// <param name="topic">
    /// The target's topic, such as a RabbitMQ routing key or Kafka topic. Null for brokers without topics.
    /// </param>
    /// <param name="message">The message envelope to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default);
}
