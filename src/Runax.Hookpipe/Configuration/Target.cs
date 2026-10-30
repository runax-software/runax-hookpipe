using System.Text.RegularExpressions;
using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Where a message is published: a broker and, for brokers that have them, a topic. Written
/// <c>broker:topic</c> (split at the first <c>:</c>), just <c>broker</c>, or as an object.
/// </summary>
/// <param name="Broker">The broker ID.</param>
/// <param name="Topic">
/// The topic, such as a RabbitMQ routing key or a Kafka topic, or a template such as
/// <c>payments.{$.type}</c>. On <c>http</c> brokers it's appended to the URL as a path. Null for
/// brokers without topics.
/// </param>
public sealed partial record Target(string Broker, string? Topic)
{
    /// <summary>
    /// The parsed topic when it has placeholders, rendered for each message. Null for a fixed topic.
    /// </summary>
    internal Template? TopicTemplate { get; init; }

    /// <summary>
    /// Whether a rendered topic stays within <c>[A-Za-z0-9._-]</c>, so a value from the request can't
    /// add RabbitMQ wildcards, path segments, or odd Kafka topic names.
    /// </summary>
    internal static bool IsSafeTopic(string topic) => SafeTopicPattern().IsMatch(topic);

    /// <summary>
    /// Compares broker and topic; the parsed template follows from the topic.
    /// </summary>
    public bool Equals(Target? other) =>
        other is not null && Broker == other.Broker && Topic == other.Topic;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Broker, Topic);

    /// <summary>
    /// Formats the target as it's written in configuration.
    /// </summary>
    public override string ToString() => Topic is null ? Broker : $"{Broker}:{Topic}";

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeTopicPattern();
}
