namespace Runax.Hookpipe.Brokers;

/// <summary>
/// Whether a broker type's targets name a topic.
/// </summary>
internal enum TopicUse
{
    /// <summary>Targets name only the broker, such as <c>console</c>.</summary>
    None,

    /// <summary>A topic is optional. On <c>http</c> it's appended to the URL as a path.</summary>
    Optional,

    /// <summary>Every target needs a topic, such as <c>rabbit:github.push</c>.</summary>
    Required,
}
