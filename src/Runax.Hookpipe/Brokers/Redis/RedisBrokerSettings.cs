using System.ComponentModel.DataAnnotations;

namespace Runax.Hookpipe.Brokers.Redis;

/// <summary>
/// Settings for a <c>redis</c> broker. Names match runax-messaging's <c>RedisConfig</c>.
/// </summary>
public sealed class RedisBrokerSettings
{
    /// <summary>
    /// The StackExchange.Redis configuration string, such as <c>localhost:6379</c>.
    /// </summary>
    [Required]
    public string Configuration { get; set; } = string.Empty;
}
