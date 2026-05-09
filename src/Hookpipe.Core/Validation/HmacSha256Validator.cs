using System.Security.Cryptography;
using System.Text;
using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates requests by computing an HMAC-SHA256 signature of the request body
/// and comparing it to the value in the configured header.
/// Handles signatures with or without an algorithm prefix (e.g. GitHub's "sha256=&lt;hex&gt;").
/// Uses constant-time comparison to prevent timing attacks.
/// Resets the request body position after reading so downstream handlers can still access it.
/// </summary>
public sealed class HmacSha256Validator(ILogger<HmacSha256Validator> logger) : IValidator
{
    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <inheritdoc />
    public string Type => "hmac-sha256";

    /// <inheritdoc />
    /// <remarks>
    /// Returns false if: config.Signature is null, the secret env var is not set,
    /// the signature header is missing, or the computed signature does not match.
    /// </remarks>
    public async Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Signature is null)
        {
            logger.LogDebug("[Hookpipe.Validator:hmac-sha256] Skipped: signature config is null");
            return false;
        }

        var secret = Environment.GetEnvironmentVariable(config.Signature.SecretEnv);
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogDebug("[Hookpipe.Validator:hmac-sha256] Failed: env var '{SecretEnv}' is not set",
                config.Signature.SecretEnv);
            return false;
        }

        if (!context.Request.Headers.TryGetValue(config.Signature.Header, out var signatureHeader))
        {
            logger.LogDebug("[Hookpipe.Validator:hmac-sha256] Failed: header '{Header}' is missing",
                config.Signature.Header);
            return false;
        }

        var signature = signatureHeader.ToString();
        var prefixIndex = signature.IndexOf('=');
        if (prefixIndex >= 0) signature = signature[(prefixIndex + 1)..];

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        using var hmac = new HMACSHA256(Utf8.GetBytes(secret));
        var hash = hmac.ComputeHash(Utf8.GetBytes(body));

        var result = CryptographicOperations.FixedTimeEquals(
            Utf8.GetBytes(Convert.ToHexString(hash).ToLowerInvariant()),
            Utf8.GetBytes(signature));

        logger.LogDebug("[Hookpipe.Validator:hmac-sha256] {Result}", result ? "Passed" : "Failed: signature mismatch");
        return result;
    }
}
