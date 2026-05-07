namespace Hookpipe.Core.Config;

/// <summary>
/// Defines a webhook endpoint that Hookpipe listens on.
/// </summary>
public sealed class EndpointConfig
{
    /// <summary>
    /// Unique identifier for this endpoint (e.g. "github-push").
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// URL path to listen on (e.g. "/github/push"). Supports path params like "/ingest/{source}".
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// HTTP methods accepted by this endpoint (e.g. "POST", "PUT"). Defaults to POST only.
    /// </summary>
    public List<string> Methods { get; set; } = ["POST"];

    /// <summary>
    /// Optional validation rules (signature verification, bearer token, etc.).
    /// </summary>
    public ValidationConfig? Validation { get; set; }

    /// <summary>
    /// ID of the sink to route messages to. Must match a <see cref="SinkConfig.Id"/>.
    /// </summary>
    public required string Sink { get; set; }

    /// <summary>
    /// Controls what data from the request is included in the produced message.
    /// </summary>
    public MessageConfig Message { get; set; } = new();
}
