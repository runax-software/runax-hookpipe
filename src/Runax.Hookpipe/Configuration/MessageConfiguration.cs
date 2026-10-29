namespace Runax.Hookpipe.Configuration;

/// <summary>
/// What a webhook's published message carries, the <c>webhooks.&lt;id&gt;.message</c> section.
/// </summary>
public sealed class MessageConfiguration
{
    /// <summary>
    /// Whether the request body is included. Defaults to true.
    /// </summary>
    public bool Body { get; init; } = true;

    /// <summary>
    /// Whether request headers are included. Defaults to false.
    /// </summary>
    public bool IncludeHeaders { get; init; }

    /// <summary>
    /// The headers to include when <see cref="IncludeHeaders"/> is true, or null for all of them.
    /// Names are case-insensitive.
    /// </summary>
    public IReadOnlyList<string>? HeaderNames { get; init; }

    /// <summary>
    /// Values attached to the message. A value can name a path parameter, such as <c>{source}</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
