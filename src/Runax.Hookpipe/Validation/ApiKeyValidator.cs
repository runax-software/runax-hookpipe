using Runax.Hookpipe.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Runax.Hookpipe.Validation;

/// <summary>
/// Validates requests by comparing a custom header value against a secret
/// stored in an environment variable.
/// Unlike bearer token validation, this supports any header name (e.g. X-API-Key).
/// Uses constant-time comparison to prevent timing attacks.
/// </summary>
public sealed class ApiKeyValidator(ILogger<ApiKeyValidator> logger) : IValidator
{
    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>
    /// The validator type identifier.
    /// </summary>
    public const string TypeName = "api-key";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <remarks>
    /// Reads the key from the header named by <see cref="ValidatorConfiguration.Header"/> and compares it
    /// with <see cref="ValidatorConfiguration.Key"/>.
    /// Returns false if: the header or key is not configured, the header is missing, or the value
    /// does not match.
    /// </remarks>
    public Task<bool> ValidateAsync(HttpContext context, ValidatorConfiguration configuration)
    {
        var headerName = configuration.Header;
        if (string.IsNullOrEmpty(headerName))
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Failed: 'header' is not configured");
            return Task.FromResult(false);
        }

        var expected = configuration.Key;
        if (string.IsNullOrEmpty(expected))
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Failed: 'key' is not configured");
            return Task.FromResult(false);
        }

        if (!context.Request.Headers.TryGetValue(headerName, out var headerValue) ||
            string.IsNullOrEmpty(headerValue.ToString()))
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Failed: header '{Header}' is missing",
                headerName);
            return Task.FromResult(false);
        }

        var result =
            CryptographicOperations.FixedTimeEquals(Utf8.GetBytes(headerValue.ToString()), Utf8.GetBytes(expected));

        logger.LogDebug("[Hookpipe.Validator:api-key] {Result}", result ? "Passed" : "Failed: key mismatch");
        return Task.FromResult(result);
    }
}
