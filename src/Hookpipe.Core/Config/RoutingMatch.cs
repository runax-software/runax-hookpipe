namespace Hookpipe.Core.Config;

/// <summary>
/// Match condition for a routing rule.
/// Matches against a request header or JSON body field using the configured operator.
/// </summary>
public sealed class RoutingMatch
{
    /// <summary>
    /// Header name to match against (e.g. "X-GitHub-Event").
    /// Mutually exclusive with <see cref="Body"/>.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>
    /// JSON body field path using dot notation (e.g. "$.event.type", "$.action").
    /// Mutually exclusive with <see cref="Header"/>.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Match operator to use. Defaults to <see cref="MatchOperator.Value"/> (exact match).
    /// </summary>
    public MatchOperator Operator { get; init; } = MatchOperator.Value;

    /// <summary>
    /// Pattern to match against. Not required when <see cref="Operator"/> is <see cref="MatchOperator.Exists"/>.
    /// </summary>
    public string? Pattern { get; init; }
}
