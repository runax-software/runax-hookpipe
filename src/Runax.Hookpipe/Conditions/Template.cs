using System.Text;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// Text with <c>{…}</c> placeholders that are filled in for each message, such as
/// <c>payments.{$.type:-other}</c>. A <c>{</c> followed by a letter, <c>_</c>, or <c>$</c> starts a
/// placeholder; <c>{{</c> and <c>}}</c> are literal braces, and any other brace is literal too. A
/// <c>{</c> right after <c>$</c> is literal, so <c>${NAME}</c> left by the <c>$${</c> escape stays
/// as written.
/// </summary>
public sealed class Template
{
    private readonly string[] _literals;
    private readonly TemplatePlaceholder[] _placeholders;

    private Template(string text, string[] literals, TemplatePlaceholder[] placeholders)
    {
        Text = text;
        _literals = literals;
        _placeholders = placeholders;
    }

    /// <summary>
    /// The template as written.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The placeholders, in order.
    /// </summary>
    public IReadOnlyList<TemplatePlaceholder> Placeholders => _placeholders;

    /// <summary>
    /// Whether the template has any placeholders. One without renders to the same text every time.
    /// </summary>
    public bool HasPlaceholders => _placeholders.Length > 0;

    /// <summary>
    /// The text between and around the placeholders, with escaped braces unescaped. There is always
    /// one more literal than there are placeholders.
    /// </summary>
    internal IReadOnlyList<string> Literals => _literals;

    internal bool UsesJsonPath => _placeholders.Any(placeholder => placeholder.Source == TemplateSource.JsonPath);

    /// <summary>
    /// Parses a template.
    /// </summary>
    /// <param name="text">The template text.</param>
    /// <returns>The parsed template.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when a placeholder has no closing brace, isn't a supported form, or has an invalid JSONPath.
    /// </exception>
    public static Template Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var literals = new List<string>();
        var placeholders = new List<TemplatePlaceholder>();
        var literal = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (character is '{' or '}' && next == character)
            {
                literal.Append(character);
                index += 2;
            }
            else if (character == '{' && (next is '$' or '_' || char.IsAsciiLetter(next)) &&
                     (index == 0 || text[index - 1] != '$'))
            {
                var end = FindClosingBrace(text, index + 1);
                if (end < 0) throw new ArgumentException($"placeholder '{text[index..]}' has no closing '}}'");

                placeholders.Add(TemplatePlaceholder.Parse(text[index..(end + 1)]));
                literals.Add(literal.ToString());
                literal.Clear();
                index = end + 1;
            }
            else
            {
                literal.Append(character);
                index++;
            }
        }

        literals.Add(literal.ToString());
        return new Template(text, [.. literals], [.. placeholders]);
    }

    /// <summary>
    /// Fills in every placeholder.
    /// </summary>
    /// <param name="context">The request the values come from.</param>
    /// <param name="rendered">The rendered text, when every placeholder resolved.</param>
    /// <param name="unresolved">The first placeholder that didn't resolve, when one didn't.</param>
    /// <returns>Whether every placeholder resolved.</returns>
    internal bool TryRender(EvaluationContext context, out string rendered, out TemplatePlaceholder? unresolved)
    {
        var builder = new StringBuilder(_literals[0]);

        for (var index = 0; index < _placeholders.Length; index++)
        {
            if (!_placeholders[index].TryResolve(context, out var value))
            {
                rendered = string.Empty;
                unresolved = _placeholders[index];
                return false;
            }

            builder.Append(value).Append(_literals[index + 1]);
        }

        rendered = builder.ToString();
        unresolved = null;
        return true;
    }

    /// <summary>
    /// Finds the brace that closes a placeholder, skipping braces inside JSONPath brackets and quotes.
    /// </summary>
    private static int FindClosingBrace(string text, int start)
    {
        var depth = 0;
        char? quote = null;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
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
                case '}' when depth <= 0:
                    return index;
            }
        }

        return -1;
    }
}
