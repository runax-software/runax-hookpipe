using System.Text.Json;
using System.Text.Json.Serialization;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Stdout;

/// <summary>
/// Broker that writes message envelopes to stdout as formatted JSON. Its targets name no topic.
/// Intended for development and debugging. No external dependencies.
/// </summary>
public sealed class StdoutBroker(ILogger<StdoutBroker> logger) : IBroker, IBrokerHealthCheck
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "stdout";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    public Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Hookpipe.Broker:stdout] Message on webhook '{WebhookId}':\n{Json}",
            message.WebhookId, JsonSerializer.Serialize(message, JsonOptions));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BrokerHealth(BrokerHealthStatus.Healthy));
}
