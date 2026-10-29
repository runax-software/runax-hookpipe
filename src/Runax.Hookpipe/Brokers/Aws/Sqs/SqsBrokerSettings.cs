using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Aws.Sqs;

/// <summary>
/// Settings for an <c>sqs</c> broker. Names match runax-messaging's <c>SqsConfig</c>.
/// </summary>
public sealed class SqsBrokerSettings : IKnownTopics
{
    /// <summary>
    /// The AWS region. Defaults to the SDK's own resolution, such as <c>AWS_REGION</c>.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// A custom endpoint, such as LocalStack's.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// The queue URL for each topic targets use. Topics are kept exactly as written.
    /// </summary>
    [Required]
    public Dictionary<string, string> TopicQueueUrlMap { get; set; } = [];

    string IKnownTopics.TopicsKey => "topic_queue_url_map";

    bool IKnownTopics.HasTopic(string topic) => TopicQueueUrlMap.ContainsKey(topic);
}
