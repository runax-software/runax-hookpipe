using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Hookpipe.Core.Validation;

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
    /// Requires <see cref="AuthValidation.Type"/> = "api-key".
    /// The header name is read from <see cref="AuthValidation.Header"/>.
    /// Returns false if: config.Auth is null, header is not set in config,
    /// the env var is not set, the header is missing, or the value does not match.
    /// </remarks>
    public Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Auth is null)
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Skipped: auth config is null");
            return Task.FromResult(false);
        }

        var headerName = config.Auth.Header;
        if (string.IsNullOrEmpty(headerName))
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Failed: 'header' is not configured");
            return Task.FromResult(false);
        }

        var expected = Environment.GetEnvironmentVariable(config.Auth.TokenEnv);
        if (string.IsNullOrEmpty(expected))
        {
            logger.LogDebug("[Hookpipe.Validator:api-key] Failed: env var '{TokenEnv}' is not set",
                config.Auth.TokenEnv);
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
