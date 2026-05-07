using Hookpipe.Core.Models;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Produces message envelopes to an external system (SQS, Kafka, RabbitMQ, etc.).
/// Each sink type implements this interface.
/// </summary>
public interface ISink
{
    /// <summary>
    /// Sink type identifier (e.g. "sqs", "kafka", "stdout"). Must match <see cref="Config.SinkConfig.Type"/>.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Sends a message envelope to the sink.
    /// </summary>
    /// <param name="message">The message envelope to produce.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
}
