using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Http;

/// <summary>
/// Settings for an <c>http</c> broker.
/// </summary>
public sealed class HttpBrokerSettings
{
    /// <summary>
    /// The URL messages are POSTed to.
    /// </summary>
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// How long a POST may take. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
