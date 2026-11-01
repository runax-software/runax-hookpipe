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
    /// Reads the JSON body through an RFC 9535 JSONPath, such as <c>$.data.object.status</c>. On a
    /// webhook that's the request body. For Hookpipe's own envelopes the payload sits under
    /// <c>$.body</c>.
    /// </summary>
    /// <param name="path">The JSONPath expression.</param>
    /// <returns>A selector waiting for its operator.</returns>
    /// <remarks>
    /// The path is parsed when the operator is applied, so a malformed one throws from
    /// <c>Eq</c>, <c>Gte</c>, and the rest rather than from here.
    /// </remarks>
    public static ConditionSelector JsonPath(string path) => new(ConditionSource.JsonPath, path);

    /// <summary>
    /// Reads a request header. The name is matched ignoring case.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>A selector waiting for its operator.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static ConditionSelector Header(string name) => new(ConditionSource.Header, name);

    /// <summary>
    /// Reads a path parameter of the webhook's route, such as <c>source</c> in
    /// <c>ingest/{source}</c>.
    /// </summary>
    /// <param name="name">The parameter name, without braces.</param>
    /// <returns>A selector waiting for its operator.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public static ConditionSelector PathParameter(string name) => new(ConditionSource.Path, name);

    /// <summary>
    /// Matches when every one of <paramref name="conditions"/> matches.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="conditions"/> is empty, or nesting would pass <see cref="MaxDepth"/>.
    /// </exception>
    public static AllCondition All(params Condition[] conditions) => new(conditions);

    /// <summary>
    /// Matches when any one of <paramref name="conditions"/> matches.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="conditions"/> is empty, or nesting would pass <see cref="MaxDepth"/>.
    /// </exception>
    public static AnyCondition Any(params Condition[] conditions) => new(conditions);

    /// <summary>
    /// Matches when <paramref name="condition"/> doesn't.
    /// </summary>
    public static NotCondition Not(Condition condition) => new(condition);

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
    internal abstract bool Evaluate(EvaluationContext context);
}
