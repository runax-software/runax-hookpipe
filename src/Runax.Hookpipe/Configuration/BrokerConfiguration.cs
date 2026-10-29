namespace Runax.Hookpipe.Configuration;

/// <summary>
/// A messaging system Hookpipe connects to, one entry of the <c>brokers</c> section. Webhooks
/// publish to it through targets such as <c>rabbit:github.push</c>.
/// </summary>
public sealed class BrokerConfiguration
{
    /// <summary>
    /// The broker type, such as <c>rabbitmq</c>, <c>kafka</c>, or <c>stdout</c>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The connection settings, bound to the settings class of <see cref="Type"/>, such as
    /// <see cref="Brokers.RabbitMq.RabbitMqBrokerSettings"/>. Null for types without settings.
    /// </summary>
    public object? Settings { get; init; }

    /// <summary>
    /// How Hookpipe publishes to this broker.
    /// </summary>
    public PublishConfiguration Publish { get; init; } = new();
}
