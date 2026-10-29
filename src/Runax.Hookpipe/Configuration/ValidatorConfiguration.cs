namespace Runax.Hookpipe.Configuration;

/// <summary>
/// One validator in a webhook's <c>validate</c> list. Which fields apply depends on
/// <see cref="Type"/>; the loader rejects fields a type doesn't use and requires the ones it does.
/// </summary>
public sealed class ValidatorConfiguration
{
    /// <summary>
    /// The validator type: <c>hmac-sha256</c>, <c>stripe-v1</c>, <c>bearer</c>, <c>api-key</c>, or
    /// <c>ip-allowlist</c>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The header holding the signature (<c>hmac-sha256</c>, <c>stripe-v1</c>) or key (<c>api-key</c>).
    /// </summary>
    public string? Header { get; init; }

    /// <summary>
    /// The signing secret for <c>hmac-sha256</c> and <c>stripe-v1</c>.
    /// </summary>
    public string? Secret { get; init; }

    /// <summary>
    /// The expected bearer token for <c>bearer</c>.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// The expected key for <c>api-key</c>.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Allowed IP addresses and CIDR ranges for <c>ip-allowlist</c>.
    /// </summary>
    public IReadOnlyList<string> Allow { get; init; } = [];
}
