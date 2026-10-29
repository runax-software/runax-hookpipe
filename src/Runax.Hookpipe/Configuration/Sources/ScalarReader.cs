using System.Globalization;
using System.Text.RegularExpressions;
using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Reads typed values from configuration nodes, adding an error with the node's path when a value
/// doesn't fit. Values filled in by interpolation are never echoed in errors.
/// </summary>
internal sealed partial class ScalarReader(ConfigurationErrors errors)
{
    private static readonly HashSet<string> Booleans = ["true", "True", "TRUE", "false", "False", "FALSE"];

    public ConfigurationErrors Errors { get; } = errors;

    public MappingReader? Mapping(ConfigurationNode node)
    {
        if (node is ConfigurationMapping mapping) return new MappingReader(mapping, Errors);

        Errors.Add(node.Path, "expected a mapping of keys and values");
        return null;
    }

    public ConfigurationSequence? Sequence(ConfigurationNode node)
    {
        if (node is ConfigurationSequence sequence) return sequence;

        Errors.Add(node.Path, "expected a list");
        return null;
    }

    public string? String(ConfigurationNode node)
    {
        if (node is ConfigurationScalar { Value: not null } scalar) return scalar.Value;

        Errors.Add(node.Path, "expected a single value");
        return null;
    }

    public int? Int(ConfigurationNode node, int minimum)
    {
        if (node is not ConfigurationScalar { Value: not null } scalar)
            return Fail<int>(node, "expected a whole number");

        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return Fail<int>(node, $"expected a whole number, got {scalar.Describe()}");

        return value >= minimum ? value : Fail<int>(node, $"must be at least {minimum}");
    }

    public double? Double(ConfigurationNode node, double minimum)
    {
        if (node is not ConfigurationScalar { Value: not null } scalar)
            return Fail<double>(node, "expected a number");

        if (!double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return Fail<double>(node, $"expected a number, got {scalar.Describe()}");

        return value >= minimum ? value : Fail<double>(node, $"must be at least {minimum}");
    }

    public bool? Bool(ConfigurationNode node)
    {
        if (node is ConfigurationScalar { Value: not null } scalar && bool.TryParse(scalar.Value, out var value))
            return value;

        return Fail<bool>(node, "expected true or false");
    }

    /// <summary>
    /// Reads a duration written <c>500ms</c>, <c>5s</c>, <c>2m</c>, or <c>1h</c>.
    /// </summary>
    public TimeSpan? Duration(ConfigurationNode node)
    {
        if (node is not ConfigurationScalar { Value: not null } scalar)
            return Fail<TimeSpan>(node, "expected a duration such as 500ms, 5s, 2m, or 1h");

        var match = DurationPattern().Match(scalar.Value.Trim());
        if (!match.Success)
            return Fail<TimeSpan>(node, $"expected a duration such as 500ms, 5s, 2m, or 1h, got {scalar.Describe()}");

        var amount = double.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        return match.Groups["unit"].Value switch
        {
            "ms" => TimeSpan.FromMilliseconds(amount),
            "s" => TimeSpan.FromSeconds(amount),
            "m" => TimeSpan.FromMinutes(amount),
            _ => TimeSpan.FromHours(amount),
        };
    }

    public TEnum? Enum<TEnum>(ConfigurationNode node) where TEnum : struct, Enum
    {
        var names = string.Join(", ", System.Enum.GetNames<TEnum>().Select(name => name.ToLowerInvariant()));

        if (node is ConfigurationScalar { Value: not null } scalar &&
            System.Enum.TryParse<TEnum>(scalar.Value.Replace("_", string.Empty), ignoreCase: true, out var value) &&
            System.Enum.IsDefined(value) && !char.IsDigit(scalar.Value[0]))
            return value;

        return Fail<TEnum>(node, $"expected one of: {names}");
    }

    /// <summary>
    /// Reads a list of single values. A plain value is read as a one-item list when
    /// <paramref name="commaSeparated"/> is set, split at commas, so <c>${ALLOWED_IPS}</c> works.
    /// </summary>
    public List<string>? StringList(ConfigurationNode node, bool commaSeparated = false)
    {
        if (commaSeparated && node is ConfigurationScalar { Value: not null } scalar)
            return scalar.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        if (Sequence(node) is not { } sequence) return null;

        var values = new List<string>();
        foreach (var item in sequence.Items)
            if (String(item) is { } value)
                values.Add(value);

        return values;
    }

    /// <summary>
    /// Reads a value a condition compares with. In YAML a plain <c>100</c> is a number and a quoted
    /// <c>"100"</c> is text; <c>IConfiguration</c> values have no type.
    /// </summary>
    public ConditionValue? Operand(ConfigurationNode node)
    {
        if (node is not ConfigurationScalar { Value: not null } scalar)
        {
            Errors.Add(node.Path, "expected a single value");
            return null;
        }

        if (scalar.Style == ConfigurationScalarStyle.Untyped)
            return new ConditionValue(scalar.Value, ConditionValueKind.Untyped);

        if (scalar.Style == ConfigurationScalarStyle.Quoted) return ConditionValue.Of(scalar.Value);

        if (Booleans.Contains(scalar.Value)) return ConditionValue.Of(bool.Parse(scalar.Value));

        return ConditionValue.TryParseNumber(scalar.Value, out _)
            ? new ConditionValue(scalar.Value, ConditionValueKind.Number)
            : ConditionValue.Of(scalar.Value);
    }

    private T? Fail<T>(ConfigurationNode node, string message) where T : struct
    {
        Errors.Add(node.Path, message);
        return null;
    }

    [GeneratedRegex(@"^(?<amount>\d+(\.\d+)?)(?<unit>ms|s|m|h)$")]
    private static partial Regex DurationPattern();
}
