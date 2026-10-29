using Runax.Hookpipe.Conditions;

namespace Runax.Hookpipe.Configuration;

/// <summary>
/// One rule of a webhook's <c>route</c>. Rules are checked in order and the first match wins.
/// </summary>
public sealed class RouteRule
{
    /// <summary>
    /// The condition, or null for the final catch-all rule.
    /// </summary>
    public Condition? When { get; init; }

    /// <summary>
    /// The targets a matching request is published to. Empty means "acknowledge and publish nothing".
    /// </summary>
    public required IReadOnlyList<Target> To { get; init; }
}
