namespace Hookpipe.Core.Config;

/// <summary>
/// Controls what data from the incoming request is included in the produced message.
/// </summary>
public sealed class MessageConfig
{
    /// <summary>
    /// Whether to include the request body in the message envelope. Defaults to true.
    /// </summary>
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// Whether to include request headers in the message envelope.
    /// If <see cref="HeaderFilter"/> is set, only matching headers are included.
    /// </summary>
    public bool IncludeHeaders { get; init; }

    /// <summary>
    /// Optional list of header names to include. When set, only these headers are forwarded.
    /// Requires <see cref="IncludeHeaders"/> to be true.
    /// </summary>
    public List<string>? HeaderFilter { get; init; }

    /// <summary>
    /// Static or path-param-derived metadata to attach to the message envelope.
    /// Values can reference path parameters using "{param}" syntax.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
