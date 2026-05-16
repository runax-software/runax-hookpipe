namespace Hookpipe.Core.Config;

/// <summary>
/// A single routing rule that maps a match condition to a set of sinks.
/// Rules are evaluated in order — first match wins.
/// </summary>
public sealed class RoutingRule
{
    /// <summary>
    /// Match condition for this rule. Null if <see cref="Default"/> is true.
    /// </summary>
    public RoutingMatch? Match { get; init; }

    /// <summary>
    /// If true, this rule matches all requests that didn't match previous rules (catch-all).
    /// </summary>
    public bool Default { get; init; }

    /// <summary>
    /// Sink IDs to route to when this rule matches.
    /// </summary>
    public List<string> Sinks { get; init; } = [];
}
