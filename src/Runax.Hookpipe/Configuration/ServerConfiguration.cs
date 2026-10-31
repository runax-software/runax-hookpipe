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

    /// <summary>
    /// The request body limit for the whole process, in bytes, or null to keep the server's own
    /// default. Only a host that owns the process applies it: the self-hosted service sets Kestrel's
    /// limit from it, and in library mode it's ignored with a warning, because the limit belongs to
    /// the app's server. Use <see cref="WebhookConfiguration.MaxBodySize"/> for a per-webhook limit.
    /// </summary>
    public long? MaxBodySize { get; init; }
}
