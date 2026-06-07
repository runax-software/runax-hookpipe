using System.Text.Json;
using System.Text.Json.Serialization;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that writes message envelopes to stdout as formatted JSON.
/// Intended for development and debugging. No external dependencies.
/// </summary>
public sealed class StdoutSink(ILogger<StdoutSink> logger) : ISink, ISinkHealthCheck
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "stdout";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    public Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Hookpipe.Sink:stdout] Message on endpoint '{EndpointId}':\n{Json}",
            message.EndpointId, JsonSerializer.Serialize(message, JsonOptions));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SinkHealth(SinkHealthStatus.Healthy));
}
