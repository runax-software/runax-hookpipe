using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Aws.EventBridge;

/// <summary>
/// Settings for an <c>eventbridge</c> broker.
/// </summary>
public sealed class EventBridgeBrokerSettings
{
    /// <summary>
    /// The event bus name or ARN.
    /// </summary>
    [Required]
    public string EventBusName { get; set; } = string.Empty;

    /// <summary>
    /// The events' <c>Source</c>. Defaults to <c>hookpipe</c>.
    /// </summary>
    public string Source { get; set; } = "hookpipe";

    /// <summary>
    /// The AWS region. Defaults to the SDK's own resolution, such as <c>AWS_REGION</c>.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// A custom endpoint, such as LocalStack's.
    /// </summary>
    public string? ServiceUrl { get; set; }
}
