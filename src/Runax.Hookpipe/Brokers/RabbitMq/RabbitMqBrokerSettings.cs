using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.RabbitMq;

/// <summary>
/// Settings for a <c>rabbitmq</c> broker. Names match runax-messaging's <c>RabbitMqConfig</c>.
/// </summary>
public sealed class RabbitMqBrokerSettings
{
    /// <summary>
    /// The AMQP URI, such as <c>amqp://user:pass@host:5672/vhost</c>.
    /// </summary>
    [Required]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// The exchange messages are published to. Declared as topic and durable. Defaults to <c>runax.messaging</c>.
    /// </summary>
    public string ExchangeName { get; set; } = "runax.messaging";
}
