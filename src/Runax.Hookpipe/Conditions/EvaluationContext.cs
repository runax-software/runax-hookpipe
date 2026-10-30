using System.Text.Json;
using System.Text.Json.Nodes;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// What conditions and templates read from one request: the webhook's ID, the request's headers and
/// path parameters, its JSON body, and the message's rendered metadata. The body is parsed on first
/// use, so a request whose rules and templates use no JSONPath never parses it.
/// </summary>
/// <param name="webhookId">The webhook's ID.</param>
/// <param name="getHeader">Returns a header's value, or null when the header is missing.</param>
/// <param name="pathParameters">The route's path parameters.</param>
/// <param name="rawBody">The request body as text, or null when it wasn't read.</param>
/// <param name="onInvalidJson">Called once when the body turns out not to be JSON.</param>
internal sealed class EvaluationContext(
    string webhookId,
    Func<string, string?> getHeader,
    IReadOnlyDictionary<string, string> pathParameters,
    string? rawBody,
    Action? onInvalidJson = null)
{
    private readonly Lazy<(bool IsJson, JsonNode? Node)> _body = new(() => Parse(rawBody, onInvalidJson));

    public string WebhookId { get; } = webhookId;

    public string? GetHeader(string name) => getHeader(name);

    public IReadOnlyDictionary<string, string> PathParameters { get; } = pathParameters;

    public JsonNode? Body => _body.Value.Node;

    /// <summary>
    /// Whether the body parsed as JSON. When it didn't, JSONPath selects nothing.
    /// </summary>
    public bool BodyIsJson => _body.Value.IsJson;

    /// <summary>
    /// The message's rendered <c>message.metadata</c>, for <c>{metadata.key}</c> placeholders in topics.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

    private static (bool, JsonNode?) Parse(string? rawBody, Action? onInvalidJson)
    {
        if (rawBody is null) return (false, null);

        try
        {
            return (true, JsonNode.Parse(rawBody));
        }
        catch (JsonException)
        {
            onInvalidJson?.Invoke();
            return (false, null);
        }
    }
}
