namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Where a message is published: a broker and, for brokers that have them, a topic. Written
/// <c>broker:topic</c> (split at the first <c>:</c>), just <c>broker</c>, or as an object.
/// </summary>
/// <param name="Broker">The broker ID.</param>
/// <param name="Topic">
/// The topic, such as a RabbitMQ routing key or a Kafka topic. On <c>http</c> brokers it's appended
/// to the URL as a path. Null for brokers without topics.
/// </param>
public sealed record Target(string Broker, string? Topic)
{
    /// <summary>
    /// Formats the target as it's written in configuration.
    /// </summary>
    public override string ToString() => Topic is null ? Broker : $"{Broker}:{Topic}";
}
