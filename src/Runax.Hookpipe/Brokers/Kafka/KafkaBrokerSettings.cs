using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Kafka;

/// <summary>
/// Settings for a <c>kafka</c> broker. Names match runax-messaging's <c>KafkaConfig</c>.
/// </summary>
public sealed class KafkaBrokerSettings
{
    /// <summary>
    /// Comma-separated <c>host:port</c> list of Kafka brokers.
    /// </summary>
    [Required]
    public string BootstrapServers { get; set; } = string.Empty;
}
