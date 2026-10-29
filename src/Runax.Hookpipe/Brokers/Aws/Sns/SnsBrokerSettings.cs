using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Aws.Sns;

/// <summary>
/// Settings for an <c>sns</c> broker. Names match runax-messaging's <c>SnsConfig</c>.
/// </summary>
public sealed class SnsBrokerSettings : IKnownTopics
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
    /// The topic ARN for each topic targets use. Topics are kept exactly as written.
    /// </summary>
    [Required]
    public Dictionary<string, string> TopicArnMap { get; set; } = [];

    string IKnownTopics.TopicsKey => "topic_arn_map";

    bool IKnownTopics.HasTopic(string topic) => TopicArnMap.ContainsKey(topic);
}
