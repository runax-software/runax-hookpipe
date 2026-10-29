namespace Runax.Hookpipe.Configuration;

/// <summary>
/// URL prefixes, the <c>server.paths</c> section.
/// </summary>
public sealed class ServerPathsConfiguration
{
    /// <summary>
    /// The default webhook prefix.
    /// </summary>
    public const string DefaultWebhooks = "/webhooks";

    /// <summary>
    /// The prefix every webhook is served under, such as <c>/webhooks</c>, or <c>/</c> for none.
    /// Starts with <c>/</c> and has no trailing <c>/</c> (except <c>/</c> itself).
    /// </summary>
    public string Webhooks { get; init; } = DefaultWebhooks;
}
