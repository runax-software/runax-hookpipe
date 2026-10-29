using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Azure.ServiceBus;

/// <summary>
/// Settings for an <c>azure-servicebus</c> broker. Names match runax-messaging's <c>AzureServiceBusConfig</c>.
/// </summary>
public sealed class ServiceBusBrokerSettings
{
    /// <summary>
    /// The Service Bus connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
