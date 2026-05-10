using System.Security.Cryptography;
using System.Text;
using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates requests by comparing the Authorization header's bearer token
/// against a secret stored in an environment variable.
/// Accepts both "Bearer &lt;token&gt;" and raw "&lt;token&gt;" formats.
/// Uses constant-time comparison to prevent timing attacks.
/// </summary>
public sealed class BearerTokenValidator(ILogger<BearerTokenValidator> logger) : IValidator
{
    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>
    /// The validator type identifier.
    /// </summary>
    public const string TypeName = "bearer";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    /// <remarks>
    /// Returns false if: config.Auth is null, the env var is not set,
    /// the Authorization header is missing or empty, or the token does not match.
    /// </remarks>
    public Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Auth is null)
        {
            logger.LogDebug("[Hookpipe.Validator:bearer] Skipped: auth config is null");
            return Task.FromResult(false);
        }

        var expected = Environment.GetEnvironmentVariable(config.Auth.TokenEnv);
        if (string.IsNullOrEmpty(expected))
        {
            logger.LogDebug("[Hookpipe.Validator:bearer] Failed: env var '{TokenEnv}' is not set",
                config.Auth.TokenEnv);
            return Task.FromResult(false);
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header))
        {
            logger.LogDebug("[Hookpipe.Validator:bearer] Failed: Authorization header is missing");
            return Task.FromResult(false);
        }

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            header = header["Bearer ".Length..].Trim();

        var result = CryptographicOperations.FixedTimeEquals(Utf8.GetBytes(header), Utf8.GetBytes(expected));

        logger.LogDebug("[Hookpipe.Validator:bearer] {Result}", result ? "Passed" : "Failed: token mismatch");
        return Task.FromResult(result);
    }
}
