namespace Runax.Hookpipe.Conditions;

/// <summary>
/// Matches when at least one condition matches.
/// </summary>
public sealed class AnyCondition : Condition
{
    /// <summary>
    /// Creates the condition.
    /// </summary>
    /// <param name="conditions">The conditions; at least one.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="conditions"/> is empty.</exception>
    public AnyCondition(IReadOnlyList<Condition> conditions)
    {
        if (conditions.Count == 0) throw new ArgumentException("'any' needs at least one condition", nameof(conditions));
        Conditions = conditions;
    }

    /// <summary>
    /// The conditions, at least one of which must match.
    /// </summary>
    public IReadOnlyList<Condition> Conditions { get; }

    internal override int Depth => 1 + Conditions.Max(condition => condition.Depth);

    internal override bool UsesJsonPath => Conditions.Any(condition => condition.UsesJsonPath);

    internal override IEnumerable<string> PathParameters =>
        Conditions.SelectMany(condition => condition.PathParameters);

    internal override bool Evaluate(ConditionContext context) =>
        Conditions.Any(condition => condition.Evaluate(context));
}
