namespace Runax.Hookpipe.Conditions;

/// <summary>
/// A condition on a webhook request, used by <c>route[].when</c>. It's either one comparison
/// (<see cref="CompareCondition"/>) or a combination of conditions (<see cref="AllCondition"/>,
/// <see cref="AnyCondition"/>, <see cref="NotCondition"/>).
/// </summary>
public abstract class Condition
{
    /// <summary>
    /// The deepest a condition may nest.
    /// </summary>
    public const int MaxDepth = 8;

    private protected Condition()
    {
    }

    /// <summary>
    /// How deeply the condition nests: 1 for a comparison.
    /// </summary>
    internal abstract int Depth { get; }

    /// <summary>
    /// Whether any comparison reads the JSON body, so the body must be parsed.
    /// </summary>
    internal abstract bool UsesJsonPath { get; }

    /// <summary>
    /// Every path parameter a comparison reads, for checking them against the route at load.
    /// </summary>
    internal abstract IEnumerable<string> PathParameters { get; }

    /// <summary>
    /// Evaluates the condition against one request.
    /// </summary>
    internal abstract bool Evaluate(ConditionContext context);
}
