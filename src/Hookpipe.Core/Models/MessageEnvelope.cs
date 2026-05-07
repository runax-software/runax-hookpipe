namespace Hookpipe.Core.Models;

/// <summary>
/// Standardized message envelope produced for every webhook received.
/// This is the payload sent to sinks.
/// </summary>
public sealed class MessageEnvelope
{
    /// <summary>
    /// Unique message ID (UUID).
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// ID of the endpoint that received this webhook.
    /// </summary>
    public required string EndpointId { get; set; }

    /// <summary>
    /// Timestamp when the request was received.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// HTTP method of the incoming request.
    /// </summary>
    public required string Method { get; set; }

    /// <summary>
    /// Request path (e.g. "/github/push").
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Remote IP address of the caller.
    /// </summary>
    public required string RemoteAddress { get; set; }

    /// <summary>
    /// Request headers (filtered based on <see cref="Config.MessageConfig"/>).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// Request body, if included.
    /// </summary>
    public object? Body { get; set; }

    /// <summary>
    /// Static or path-param-derived metadata from endpoint config.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
