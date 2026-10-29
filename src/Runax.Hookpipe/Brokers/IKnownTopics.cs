namespace Runax.Hookpipe.Brokers;

/// <summary>
/// Implemented by settings that list their topics up front, so the loader can reject a target whose
/// topic the broker can't publish to.
/// </summary>
internal interface IKnownTopics
{
    /// <summary>
    /// The settings key that lists the topics, such as <c>topic_queue_url_map</c>.
    /// </summary>
    string TopicsKey { get; }

    /// <summary>
    /// Whether the broker can publish to a topic.
    /// </summary>
    bool HasTopic(string topic);
}
