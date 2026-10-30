using Runax.Hookpipe.Conditions;

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
    /// Values attached to the message. Values are templates, so they can hold a path parameter such as
    /// <c>{source}</c>, <c>{webhook_id}</c>, <c>{header.X-Name}</c>, or <c>{$.json.path}</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The parsed <see cref="Metadata"/> values, keyed like it. Filled in by the loader; a key missing
    /// here is parsed when the message is built.
    /// </summary>
    internal IReadOnlyDictionary<string, Template> MetadataTemplates { get; init; } = new Dictionary<string, Template>();

    /// <summary>
    /// The parsed template for a metadata key.
    /// </summary>
    internal Template MetadataTemplate(string key) =>
        MetadataTemplates.TryGetValue(key, out var template) ? template : Template.Parse(Metadata[key]);
}
