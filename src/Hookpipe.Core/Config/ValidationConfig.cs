namespace Hookpipe.Core.Config;

/// <summary>
/// Validation rules for an endpoint. At most one method should be configured.
/// </summary>
public sealed class ValidationConfig
{
    /// <summary>
    /// HMAC or provider-specific signature verification (e.g. GitHub, Stripe).
    /// </summary>
    public SignatureValidation? Signature { get; set; }

    /// <summary>
    /// Simple token-based authentication (e.g. bearer token).
    /// </summary>
    public AuthValidation? Auth { get; set; }
}
