namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Process-wide settings, the <c>server</c> section.
/// </summary>
public sealed class ServerConfiguration
{
    /// <summary>
    /// URL prefixes Hookpipe's routes are served under.
    /// </summary>
    public ServerPathsConfiguration Paths { get; init; } = new();
}
