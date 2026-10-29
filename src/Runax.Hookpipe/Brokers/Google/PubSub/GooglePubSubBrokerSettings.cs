using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Google.PubSub;

/// <summary>
/// Settings for a <c>google-pubsub</c> broker. Names match runax-messaging's <c>GooglePubSubConfig</c>.
/// </summary>
public sealed class GooglePubSubBrokerSettings
{
    /// <summary>
    /// The Google Cloud project that owns the topics.
    /// </summary>
    [Required]
    public string ProjectId { get; set; } = string.Empty;
}
