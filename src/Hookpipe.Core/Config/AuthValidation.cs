namespace Hookpipe.Core.Config;

/// <summary>
/// Token-based authentication for an endpoint.
/// </summary>
public sealed class AuthValidation
{
    /// <summary>
    /// Authentication type (e.g. "bearer").
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Environment variable name holding the expected token value.
    /// </summary>
    public required string TokenEnv { get; set; }
}
