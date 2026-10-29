using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Azure.EventHubs;

/// <summary>
/// Settings for an <c>azure-eventhubs</c> broker. Names match runax-messaging's <c>AzureEventHubsConfig</c>.
/// </summary>
public sealed class EventHubsBrokerSettings
{
    /// <summary>
    /// The Event Hubs namespace connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
