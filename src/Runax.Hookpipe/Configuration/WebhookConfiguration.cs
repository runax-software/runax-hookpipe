namespace Runax.Hookpipe.Configuration;

/// <summary>
/// A webhook Hookpipe receives, one entry of the <c>webhooks</c> section. It's served at
/// <c>{server.paths.webhooks}/{path}</c> and publishes each request to its targets.
/// </summary>
public sealed class WebhookConfiguration
{
    /// <summary>
    /// The route under <see cref="ServerPathsConfiguration.Webhooks"/>, without a leading <c>/</c>, such
    /// as <c>github</c> or <c>ingest/{source}</c>. The loader sets it to the webhook ID when the
    /// configuration leaves it out.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The HTTP methods accepted, in upper case. Defaults to POST only.
    /// </summary>
    public IReadOnlyList<string> Methods { get; init; } = ["POST"];

    /// <summary>
    /// Validators that must all pass before a request is accepted.
    /// </summary>
    public IReadOnlyList<ValidatorConfiguration> Validate { get; init; } = [];

    /// <summary>
    /// The rate limit, or null for none.
    /// </summary>
    public RateLimitConfiguration? RateLimit { get; init; }

    /// <summary>
    /// The request body limit for this webhook, in bytes, or null to keep the host's limit. A larger
    /// request gets 413. It's applied per request, so it works in library mode too.
    /// </summary>
    public long? MaxBodySize { get; init; }

    /// <summary>
    /// What the published message carries.
    /// </summary>
    public MessageConfiguration Message { get; init; } = new();

    /// <summary>
    /// Targets every request is published to. Exactly one of <see cref="To"/> and
    /// <see cref="Route"/> is set; an empty list means "acknowledge and publish nothing".
    /// </summary>
    public IReadOnlyList<Target>? To { get; init; }

    /// <summary>
    /// Ordered rules; the first whose condition matches picks the targets. The last rule has no
    /// condition. Exactly one of <see cref="To"/> and <see cref="Route"/> is set.
    /// </summary>
    public IReadOnlyList<RouteRule>? Route { get; init; }
}
