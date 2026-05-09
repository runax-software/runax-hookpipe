namespace Hookpipe.Core.Validation;

/// <summary>
/// Creates all available validators.
/// </summary>
public static class ValidatorFactory
{
    /// <summary>
    /// Returns a dictionary of all registered validators keyed by type string.
    /// </summary>
    /// <returns>Dictionary of validators keyed by type (e.g. "bearer", "hmac-sha256").</returns>
    public static Dictionary<string, IValidator> CreateAll()
    {
        return new Dictionary<string, IValidator>
        {
            ["bearer"] = new BearerTokenValidator(),
            ["hmac-sha256"] = new HmacSha256Validator(),
        };
    }
}
