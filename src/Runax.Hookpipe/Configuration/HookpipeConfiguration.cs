namespace Runax.Hookpipe.Configuration;

/// <summary>
/// A loaded Hookpipe definition: the brokers it connects to and the webhooks that publish to them.
/// IDs are compared case-insensitively.
/// </summary>
public sealed class HookpipeConfiguration
{
    /// <summary>
    /// The configuration format version this release reads. Files must declare <c>version: 2</c>.
    /// </summary>
    public const int SupportedVersion = 2;

    /// <summary>
    /// Process-wide settings such as the URL prefix webhooks are served under.
    /// </summary>
    public ServerConfiguration Server { get; init; } = new();

    /// <summary>
    /// The messaging systems Hookpipe connects to, keyed by broker ID.
    /// </summary>
    public IReadOnlyDictionary<string, BrokerConfiguration> Brokers { get; init; } =
        new Dictionary<string, BrokerConfiguration>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The webhooks Hookpipe receives, keyed by webhook ID.
    /// </summary>
    public IReadOnlyDictionary<string, WebhookConfiguration> Webhooks { get; init; } =
        new Dictionary<string, WebhookConfiguration>(StringComparer.OrdinalIgnoreCase);
}
