using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Path;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// One comparison, such as <c>{ jsonpath: "$.status", eq: paid }</c>. When a JSONPath selects several
/// values, the comparison holds if any of them matches.
/// </summary>
public sealed class CompareCondition : Condition
{
    /// <summary>
    /// How long a <see cref="ConditionOperator.Regex"/> match may run before it counts as no match.
    /// </summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly JsonPath? _jsonPath;
    private readonly Regex? _regex;

    /// <summary>
    /// Creates the comparison, parsing a JSONPath or regex up front.
    /// </summary>
    /// <param name="source">Where the value comes from.</param>
    /// <param name="selector">The JSONPath, header name, or path parameter name.</param>
    /// <param name="operator">How the value is matched.</param>
    /// <param name="operands">
    /// The values compared with: one for most operators, one or more for <see cref="ConditionOperator.In"/>
    /// and <see cref="ConditionOperator.NotIn"/>, a boolean for <see cref="ConditionOperator.Exists"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the selector is empty, the JSONPath or regex doesn't parse, or the operands don't
    /// fit the operator.
    /// </exception>
    public CompareCondition(
        ConditionSource source,
        string selector,
        ConditionOperator @operator,
        IReadOnlyList<ConditionValue> operands)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ArgumentException("the selector is empty", nameof(selector));

        Source = source;
        Selector = selector;
        Operator = @operator;
        Operands = operands;

        CheckOperands(@operator, operands);

        if (source == ConditionSource.JsonPath) _jsonPath = ParseJsonPath(selector);
        if (@operator == ConditionOperator.Regex) _regex = ParseRegex(operands[0].Text);
    }

    /// <summary>
    /// Where the value comes from.
    /// </summary>
    public ConditionSource Source { get; }

    /// <summary>
    /// The JSONPath, header name, or path parameter name.
    /// </summary>
    public string Selector { get; }

    /// <summary>
    /// How the value is matched.
    /// </summary>
    public ConditionOperator Operator { get; }

    /// <summary>
    /// The values compared with.
    /// </summary>
    public IReadOnlyList<ConditionValue> Operands { get; }

    internal override int Depth => 1;

    internal override bool UsesJsonPath => Source == ConditionSource.JsonPath;

    internal override IEnumerable<string> PathParameters =>
        Source == ConditionSource.Path ? [Selector] : [];

    internal override bool Evaluate(ConditionContext context)
    {
        var selected = Select(context);

        return Operator switch
        {
            ConditionOperator.Exists =>
                selected.Any(value => value.Kind != SelectedKind.Null) ==
                (Operands[0].TryGetBoolean(out var exists) && exists),
            ConditionOperator.Ne =>
                selected.Count > 0 && selected.Any(value => !AreEqual(value, Operands[0])),
            ConditionOperator.NotIn =>
                selected.Count > 0 && selected.Any(value => Operands.All(operand => !AreEqual(value, operand))),
            _ => selected.Any(Matches),
        };
    }

    private bool Matches(SelectedValue value) => Operator switch
    {
        ConditionOperator.Eq => AreEqual(value, Operands[0]),
        ConditionOperator.In => Operands.Any(operand => AreEqual(value, operand)),
        ConditionOperator.Gt => Compare(value) is > 0,
        ConditionOperator.Gte => Compare(value) is >= 0,
        ConditionOperator.Lt => Compare(value) is < 0,
        ConditionOperator.Lte => Compare(value) is <= 0,
        ConditionOperator.Contains => HasText(value) &&
                                      value.Text!.Contains(Operands[0].Text, StringComparison.OrdinalIgnoreCase),
        ConditionOperator.StartsWith => HasText(value) &&
                                        value.Text!.StartsWith(Operands[0].Text, StringComparison.OrdinalIgnoreCase),
        ConditionOperator.EndsWith => HasText(value) &&
                                      value.Text!.EndsWith(Operands[0].Text, StringComparison.OrdinalIgnoreCase),
        ConditionOperator.Regex => HasText(value) && IsRegexMatch(value.Text!),
        _ => false,
    };

    private List<SelectedValue> Select(ConditionContext context)
    {
        switch (Source)
        {
            case ConditionSource.Header:
                var header = context.GetHeader(Selector);
                return string.IsNullOrEmpty(header) ? [] : [new SelectedValue(header, SelectedKind.Untyped)];

            case ConditionSource.Path:
                return context.PathParameters.TryGetValue(Selector, out var parameter) && parameter.Length > 0
                    ? [new SelectedValue(parameter, SelectedKind.Untyped)]
                    : [];

            default:
                if (!context.BodyIsJson) return [];
                var result = _jsonPath!.Evaluate(context.Body);
                return result.Matches.Select(match => FromJson(match.Value)).ToList();
        }
    }

    private static SelectedValue FromJson(JsonNode? node)
    {
        if (node is not JsonValue value)
            return node is null
                ? new SelectedValue(null, SelectedKind.Null)
                : new SelectedValue(node.ToJsonString(), SelectedKind.Structured);

        return value.GetValueKind() switch
        {
            JsonValueKind.String => new SelectedValue(value.GetValue<string>(), SelectedKind.String),
            JsonValueKind.Number => new SelectedValue(value.ToJsonString(), SelectedKind.Number),
            JsonValueKind.True => new SelectedValue("true", SelectedKind.Boolean),
            JsonValueKind.False => new SelectedValue("false", SelectedKind.Boolean),
            _ => new SelectedValue(null, SelectedKind.Null),
        };
    }

    private static bool AreEqual(SelectedValue value, ConditionValue operand)
    {
        if (value.Text is null || value.Kind == SelectedKind.Null) return false;

        if (value.Kind == SelectedKind.Untyped || operand.Kind == ConditionValueKind.Untyped)
        {
            return value.Text == operand.Text
                   || (ConditionValue.TryParseNumber(value.Text, out var left) && operand.TryGetNumber(out var right) &&
                       left == right)
                   || (bool.TryParse(value.Text, out var leftFlag) && operand.TryGetBoolean(out var rightFlag) &&
                       leftFlag == rightFlag);
        }

        return operand.Kind switch
        {
            ConditionValueKind.Number => value.Kind == SelectedKind.Number &&
                                         ConditionValue.TryParseNumber(value.Text, out var number) &&
                                         operand.TryGetNumber(out var expected) && number == expected,
            ConditionValueKind.Boolean => value.Kind == SelectedKind.Boolean && value.Text == operand.Text,
            _ => value.Kind != SelectedKind.Structured && string.Equals(value.Text, operand.Text, StringComparison.Ordinal),
        };
    }

    private int? Compare(SelectedValue value)
    {
        if (value.Kind is not (SelectedKind.Number or SelectedKind.Untyped)) return null;
        if (!ConditionValue.TryParseNumber(value.Text, out var number)) return null;

        return Operands[0].TryGetNumber(out var operand) ? number.CompareTo(operand) : null;
    }

    private static bool HasText(SelectedValue value) => value.Text is not null && value.Kind != SelectedKind.Null;

    private bool IsRegexMatch(string text)
    {
        try
        {
            return _regex!.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static void CheckOperands(ConditionOperator @operator, IReadOnlyList<ConditionValue> operands)
    {
        switch (@operator)
        {
            case ConditionOperator.In or ConditionOperator.NotIn:
                if (operands.Count == 0) throw new ArgumentException($"'{Name(@operator)}' needs a list of values");
                return;

            case ConditionOperator.Exists:
                if (operands.Count != 1 || !IsBoolean(operands[0]))
                    throw new ArgumentException("'exists' needs true or false");
                return;

            case ConditionOperator.Gt or ConditionOperator.Gte or ConditionOperator.Lt or ConditionOperator.Lte:
                if (operands.Count != 1 || operands[0].Kind is ConditionValueKind.Text or ConditionValueKind.Boolean ||
                    !operands[0].TryGetNumber(out _))
                    throw new ArgumentException($"'{Name(@operator)}' needs a number");
                return;

            default:
                if (operands.Count != 1) throw new ArgumentException($"'{Name(@operator)}' needs one value");
                return;
        }
    }

    private static bool IsBoolean(ConditionValue operand) =>
        operand.Kind is ConditionValueKind.Boolean or ConditionValueKind.Untyped && operand.TryGetBoolean(out _);

    private static JsonPath ParseJsonPath(string selector)
    {
        if (!selector.StartsWith('$'))
            throw new ArgumentException($"JSONPath '{selector}' must start with '$', for example '$.{selector}'");

        try
        {
            return JsonPath.Parse(selector);
        }
        catch (Exception ex) when (ex is PathParseException or ArgumentException)
        {
            throw new ArgumentException($"invalid JSONPath '{selector}': {ex.Message}", ex);
        }
    }

    private static Regex ParseRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"invalid regex: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The operator's name as written in configuration, such as <c>not_in</c>.
    /// </summary>
    internal static string Name(ConditionOperator @operator) => @operator switch
    {
        ConditionOperator.NotIn => "not_in",
        ConditionOperator.StartsWith => "starts_with",
        ConditionOperator.EndsWith => "ends_with",
        _ => @operator.ToString().ToLowerInvariant(),
    };

    private enum SelectedKind
    {
        String,
        Number,
        Boolean,
        Null,
        Structured,
        Untyped,
    }

    private readonly record struct SelectedValue(string? Text, SelectedKind Kind);
}
