namespace Hookpipe.Core.Config;

/// <summary>
/// Operators for routing match conditions.
/// </summary>
public enum MatchOperator
{
    /// <summary>
    /// Exact string match (case-sensitive).
    /// </summary>
    Value,

    /// <summary>
    /// Substring match (case-insensitive).
    /// </summary>
    Contains,

    /// <summary>
    /// Prefix match (case-insensitive).
    /// </summary>
    StartsWith,

    /// <summary>
    /// Regular expression match.
    /// </summary>
    Regex,

    /// <summary>
    /// Checks if the header or body field exists (non-empty). No pattern required.
    /// </summary>
    Exists,
}
