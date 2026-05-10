namespace Hookpipe.Core.Config;

/// <summary>
/// Defines a message sink (SQS, Kafka, RabbitMQ, etc.).
/// </summary>
public sealed class SinkConfig
{
    /// <summary>
    /// Unique identifier for this sink, referenced by <see cref="EndpointConfig.Sink"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Sink type (e.g. "sqs", "kafka", "rabbitmq", "http", "stdout").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Sink-specific settings. Keys and values depend on the sink type.
    /// Values referencing environment variables should use the "_env" suffix convention.
    /// </summary>
    public Dictionary<string, string> Settings { get; init; } = [];
}
