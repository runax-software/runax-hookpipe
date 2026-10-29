using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runax.Hookpipe.Brokers;

/// <summary>
/// Shared utilities for broker implementations.
/// </summary>
internal static class BrokerHelper
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns the target's topic, or throws when the target has none.
    /// </summary>
    internal static string RequireTopic(string? topic, string type, string brokerId) =>
        string.IsNullOrEmpty(topic)
            ? throw new ArgumentException($"Broker '{brokerId}' ({type}) needs a topic in every target", nameof(topic))
            : topic;
}
