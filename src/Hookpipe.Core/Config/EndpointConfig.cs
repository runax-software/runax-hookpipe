namespace Hookpipe.Core.Config;

/// <summary>
/// Defines a webhook endpoint that Hookpipe listens on.
/// </summary>
public sealed class EndpointConfig
{
    /// <summary>
    /// Unique identifier for this endpoint (e.g. "github-push").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// URL path to listen on (e.g. "/github/push"). Supports path params like "/ingest/{source}".
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// HTTP methods accepted by this endpoint (e.g. "POST", "PUT"). Defaults to POST only.
    /// </summary>
    public List<string> Methods { get; set; } = ["POST"];

    /// <summary>
    /// Optional validation rules (signature verification, bearer token, etc.).
    /// </summary>
    public ValidationConfig? Validation { get; set; }

    /// <summary>
    /// Single sink ID for backwards compatibility. Use <see cref="Sinks"/> for fan-out.
    /// </summary>
    public string? Sink { get; set; }

    /// <summary>
    /// Sink IDs to route messages to. Must match <see cref="SinkConfig.Id"/>.
    /// If empty, falls back to <see cref="Sink"/>.
    /// </summary>
    public List<string> Sinks { get; set; } = [];

    /// <summary>
    /// Returns the resolved list of sink IDs (merges Sink and Sinks).
    /// </summary>
    public List<string> GetResolvedSinks()
    {
        if (Sinks.Count > 0) return Sinks;
        if (!string.IsNullOrWhiteSpace(Sink)) return [Sink];

        return [];
    }

    /// <summary>
    /// Controls what data from the request is included in the produced message.
    /// </summary>
    public MessageConfig Message { get; init; } = new();
}
