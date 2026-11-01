namespace Runax.Hookpipe.Conditions;

/// <summary>
/// A chosen condition source, waiting for its operator. Produced by
/// <see cref="Condition.JsonPath"/>, <see cref="Condition.Header"/>, and
/// <see cref="Condition.PathParameter"/>, so a condition reads the way it does in YAML:
/// <c>Condition.JsonPath("$.status").Eq("paid")</c>.
/// </summary>
/// <remarks>
/// Every operator validates immediately, as the loader does: a JSONPath that doesn't parse, a regex
/// that doesn't compile, a numeric comparison against text, or a placeholder that isn't a path
/// parameter all throw here rather than failing at startup or on a request.
/// </remarks>
public sealed class ConditionSelector
{
    private readonly ConditionSource _source;
    private readonly string _selector;

    internal ConditionSelector(ConditionSource source, string selector)
    {
        _source = source;
        _selector = selector;
    }

    /// <summary>Matches when the value equals <paramref name="value"/>.</summary>
    public CompareCondition Eq(string value) => Compare(ConditionOperator.Eq, ConditionValue.Of(value));

    /// <summary>Matches when the value equals <paramref name="value"/>, compared as a number.</summary>
    public CompareCondition Eq(decimal value) => Compare(ConditionOperator.Eq, ConditionValue.Of(value));

    /// <summary>Matches when the value equals <paramref name="value"/>, compared as a boolean.</summary>
    public CompareCondition Eq(bool value) => Compare(ConditionOperator.Eq, ConditionValue.Of(value));

    /// <summary>
    /// Matches when the value differs from <paramref name="value"/>. False when nothing is selected;
    /// use <c>Condition.Not(... .Eq(value))</c> for "missing or different".
    /// </summary>
    public CompareCondition Ne(string value) => Compare(ConditionOperator.Ne, ConditionValue.Of(value));

    /// <summary>Matches when the value equals one of <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public CompareCondition In(params string[] values) => Compare(ConditionOperator.In, Values(values));

    /// <summary>Matches when the value equals none of <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public CompareCondition NotIn(params string[] values) => Compare(ConditionOperator.NotIn, Values(values));

    /// <summary>Matches when the value is a number greater than <paramref name="value"/>.</summary>
    public CompareCondition Gt(decimal value) => Compare(ConditionOperator.Gt, ConditionValue.Of(value));

    /// <summary>Matches when the value is a number at or above <paramref name="value"/>.</summary>
    public CompareCondition Gte(decimal value) => Compare(ConditionOperator.Gte, ConditionValue.Of(value));

    /// <summary>Matches when the value is a number below <paramref name="value"/>.</summary>
    public CompareCondition Lt(decimal value) => Compare(ConditionOperator.Lt, ConditionValue.Of(value));

    /// <summary>Matches when the value is a number at or below <paramref name="value"/>.</summary>
    public CompareCondition Lte(decimal value) => Compare(ConditionOperator.Lte, ConditionValue.Of(value));

    /// <summary>Matches when the text contains <paramref name="value"/>, ignoring case.</summary>
    public CompareCondition Contains(string value) => Compare(ConditionOperator.Contains, ConditionValue.Of(value));

    /// <summary>Matches when the text starts with <paramref name="value"/>, ignoring case.</summary>
    public CompareCondition StartsWith(string value) => Compare(ConditionOperator.StartsWith, ConditionValue.Of(value));

    /// <summary>Matches when the text ends with <paramref name="value"/>, ignoring case.</summary>
    public CompareCondition EndsWith(string value) => Compare(ConditionOperator.EndsWith, ConditionValue.Of(value));

    /// <summary>
    /// Matches when the .NET regex <paramref name="pattern"/> matches, case-sensitively. Compiled
    /// here, with a 100 ms match timeout. Patterns are never templates.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> doesn't compile.</exception>
    public CompareCondition Regex(string pattern) => Compare(ConditionOperator.Regex, ConditionValue.Of(pattern));

    /// <summary>
    /// Matches when at least one non-null value is selected, or when <paramref name="exists"/> is
    /// false, when none is.
    /// </summary>
    public CompareCondition Exists(bool exists = true) =>
        Compare(ConditionOperator.Exists, ConditionValue.Of(exists));

    private static IReadOnlyList<ConditionValue> Values(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Length == 0
            ? throw new ArgumentException("at least one value is required", nameof(values))
            : [.. values.Select(ConditionValue.Of)];
    }

    private CompareCondition Compare(ConditionOperator @operator, ConditionValue value) =>
        Compare(@operator, [value]);

    private CompareCondition Compare(ConditionOperator @operator, IReadOnlyList<ConditionValue> operands) =>
        new(_source, _selector, @operator, operands);
}
