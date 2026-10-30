namespace Runax.Hookpipe.Conditions;

/// <summary>
/// Matches when its condition doesn't.
/// </summary>
/// <param name="condition">The condition to negate.</param>
public sealed class NotCondition(Condition condition) : Condition
{
    /// <summary>
    /// The negated condition.
    /// </summary>
    public Condition Condition { get; } = condition;

    internal override int Depth => 1 + Condition.Depth;

    internal override bool UsesJsonPath => Condition.UsesJsonPath;

    internal override IEnumerable<string> PathParameters => Condition.PathParameters;

    internal override bool Evaluate(EvaluationContext context) => !Condition.Evaluate(context);
}
