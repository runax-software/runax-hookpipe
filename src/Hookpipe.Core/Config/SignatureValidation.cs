namespace Hookpipe.Core.Config;

/// <summary>
/// Signature-based request validation (HMAC, Stripe, etc.).
/// </summary>
public sealed class SignatureValidation
{
    /// <summary>
    /// HTTP header containing the signature (e.g. "X-Hub-Signature-256").
    /// </summary>
    public required string Header { get; init; }

    /// <summary>
    /// Environment variable name holding the signing secret.
    /// </summary>
    public required string SecretEnv { get; init; }

    /// <summary>
    /// Signature algorithm (e.g. "hmac-sha256", "stripe-v1").
    /// </summary>
    public required string Algorithm { get; init; }
}
