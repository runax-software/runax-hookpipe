using System.Text.Json.Nodes;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// What conditions read from one request: its headers, path parameters, and parsed JSON body.
/// </summary>
/// <param name="getHeader">Returns a header's value, or null when the header is missing.</param>
/// <param name="pathParameters">The route's path parameters.</param>
/// <param name="body">The parsed body, when it's JSON.</param>
/// <param name="bodyIsJson">Whether the body parsed as JSON. When it didn't, JSONPath selects nothing.</param>
internal sealed class ConditionContext(
    Func<string, string?> getHeader,
    IReadOnlyDictionary<string, string> pathParameters,
    JsonNode? body,
    bool bodyIsJson)
{
    public string? GetHeader(string name) => getHeader(name);

    public IReadOnlyDictionary<string, string> PathParameters { get; } = pathParameters;

    public JsonNode? Body { get; } = body;

    public bool BodyIsJson { get; } = bodyIsJson;
}
