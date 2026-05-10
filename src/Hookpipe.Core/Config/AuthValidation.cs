namespace Hookpipe.Core.Config;

/// <summary>
/// Token-based authentication for an endpoint.
/// </summary>
public sealed class AuthValidation
{
    /// <summary>
    /// Authentication type (e.g. "bearer").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Environment variable name holding the expected token value.
    /// </summary>
    public required string TokenEnv { get; init; }

    /// <summary>
    /// Custom header name to read the token from (used by "api-key" type).
    /// Ignored by "bearer" which always uses the Authorization header.
    /// </summary>
    public string? Header { get; set; }
}
