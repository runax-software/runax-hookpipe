using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Path;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// One <c>{…}</c> placeholder in a <see cref="Template"/>, such as <c>{source}</c> or
/// <c>{$.type:-other}</c>.
/// </summary>
public sealed partial class TemplatePlaceholder
{
    private const string DefaultSeparator = ":-";
    private const string MetadataPrefix = "metadata.";
    private const string HeaderPrefix = "header.";

    private readonly JsonPath? _jsonPath;

    private TemplatePlaceholder(string text, TemplateSource source, string name, string? defaultValue, JsonPath? jsonPath)
    {
        Text = text;
        Source = source;
        Name = name;
        Default = defaultValue;
        _jsonPath = jsonPath;
    }

    /// <summary>
    /// The placeholder as written, braces included.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Where the value comes from.
    /// </summary>
    public TemplateSource Source { get; }

    /// <summary>
    /// The path parameter, metadata key, header name, or JSONPath. Empty for <c>{webhook_id}</c> and
    /// <c>{topic}</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The value used when the placeholder doesn't resolve, written after <c>:-</c>. Null when there's none.
    /// </summary>
    public string? Default { get; }

    /// <summary>
    /// Parses one placeholder.
    /// </summary>
    /// <param name="text">The placeholder, braces included.</param>
    /// <exception cref="ArgumentException">Thrown when the placeholder isn't one of the supported forms.</exception>
    internal static TemplatePlaceholder Parse(string text)
    {
        var (expression, defaultValue) = SplitDefault(text[1..^1]);

        if (expression.StartsWith('$'))
        {
            try
            {
                return new TemplatePlaceholder(text, TemplateSource.JsonPath, expression, defaultValue,
                    CompareCondition.ParseJsonPath(expression));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"placeholder '{text}': {ex.Message}", ex);
            }
        }

        if (string.Equals(expression, "webhook_id", StringComparison.OrdinalIgnoreCase))
            return new TemplatePlaceholder(text, TemplateSource.WebhookId, string.Empty, defaultValue, null);

        if (string.Equals(expression, "topic", StringComparison.OrdinalIgnoreCase))
            return new TemplatePlaceholder(text, TemplateSource.Topic, string.Empty, defaultValue, null);

        if (expression.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase) &&
            expression.Length > MetadataPrefix.Length)
            return new TemplatePlaceholder(text, TemplateSource.Metadata, expression[MetadataPrefix.Length..],
                defaultValue, null);

        if (expression.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) &&
            HeaderNamePattern().IsMatch(expression[HeaderPrefix.Length..]))
            return new TemplatePlaceholder(text, TemplateSource.Header, expression[HeaderPrefix.Length..],
                defaultValue, null);

        if (ParameterNamePattern().IsMatch(expression))
            return new TemplatePlaceholder(text, TemplateSource.PathParameter, expression, defaultValue, null);

        throw new ArgumentException(
            $"placeholder '{text}' isn't valid; use {{name}} for a path parameter, {{webhook_id}}, " +
            "{metadata.key}, {header.Name}, or {$.json.path}");
    }

    /// <summary>
    /// Resolves the placeholder, falling back to <see cref="Default"/>. Missing and empty values don't
    /// resolve, and neither does a JSONPath that selects anything but exactly one scalar.
    /// </summary>
    internal bool TryResolve(EvaluationContext context, out string value)
    {
        var resolved = Source switch
        {
            TemplateSource.PathParameter => context.PathParameters.GetValueOrDefault(Name),
            TemplateSource.WebhookId => context.WebhookId,
            TemplateSource.Metadata => context.Metadata.GetValueOrDefault(Name),
            TemplateSource.Header => context.GetHeader(Name),
            TemplateSource.JsonPath => SelectScalar(context),
            _ => null,
        };

        value = string.IsNullOrEmpty(resolved) ? Default ?? string.Empty : resolved;
        return !string.IsNullOrEmpty(resolved) || Default is not null;
    }

    private string? SelectScalar(EvaluationContext context)
    {
        if (!context.BodyIsJson) return null;

        var matches = _jsonPath!.Evaluate(context.Body).Matches;
        if (matches.Count != 1 || matches[0].Value is not JsonValue value) return null;

        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    /// <summary>
    /// Splits <c>expression:-default</c> at the first <c>:-</c> outside brackets and quotes, so a
    /// JSONPath slice such as <c>$.items[0:-1]</c> stays whole.
    /// </summary>
    private static (string Expression, string? Default) SplitDefault(string content)
    {
        var depth = 0;
        char? quote = null;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quote is not null)
            {
                if (character == '\\') index++;
                else if (character == quote) quote = null;
                continue;
            }

            switch (character)
            {
                case '\'' or '"':
                    quote = character;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ':' when depth == 0 && content.AsSpan(index).StartsWith(DefaultSeparator):
                    return (content[..index], content[(index + DefaultSeparator.Length)..]);
            }
        }

        return (content, null);
    }

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex ParameterNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+.^_`|~-]+$")]
    private static partial Regex HeaderNamePattern();
}
