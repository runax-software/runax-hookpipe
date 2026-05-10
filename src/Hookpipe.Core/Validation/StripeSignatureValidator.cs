using System.Security.Cryptography;
using System.Text;
using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates Stripe webhook signatures using Stripe's v1 signing scheme.
/// Verifies both the HMAC-SHA256 signature and timestamp freshness.
/// Header format: "t=timestamp,v1=signature"
/// </summary>
public sealed class StripeSignatureValidator(ILogger<StripeSignatureValidator> logger) : IValidator
{
    private static readonly Encoding Utf8 = Encoding.UTF8;
    private const int DefaultToleranceSeconds = 300; // 5 minutes

    public const string TypeName = "stripe-v1";

    /// <inheritdoc />
    public string Type => TypeName;

    /// <inheritdoc />
    /// <remarks>
    /// Returns false if: config.Signature is null, the secret env var is not set,
    /// the Stripe-Signature header is missing or malformed, the timestamp is too old,
    /// or the computed signature does not match.
    /// </remarks>
    public async Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Signature is null)
        {
            logger.LogDebug("[Hookpipe.Validator:stripe-v1] Skipped: signature config is null");
            return false;
        }

        var secret = Environment.GetEnvironmentVariable(config.Signature.SecretEnv);
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogDebug("[Hookpipe.Validator:stripe-v1] Failed: env var '{SecretEnv}' is not set",
                config.Signature.SecretEnv);
            return false;
        }

        var header = config.Signature.Header ?? "Stripe-Signature";
        if (!context.Request.Headers.TryGetValue(header, out var signatureHeader))
        {
            logger.LogDebug("[Hookpipe.Validator:stripe-v1] Failed: header '{Header}' is missing", header);
            return false;
        }

        var headerValue = signatureHeader.ToString();
        if (!TryParseStripeHeader(headerValue, out var timestamp, out var signature))
        {
            logger.LogDebug("[Hookpipe.Validator:stripe-v1] Failed: malformed Stripe-Signature header");
            return false;
        }

        // Check timestamp freshness
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > DefaultToleranceSeconds)
        {
            logger.LogDebug("[Hookpipe.Validator:stripe-v1] Failed: timestamp too old ({TimestampAge}s)",
                Math.Abs(now - timestamp));
            return false;
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        // Stripe signed payload: "timestamp.body"
        var signedPayload = $"{timestamp}.{body}";
        using var hmac = new HMACSHA256(Utf8.GetBytes(secret));
        var hash = hmac.ComputeHash(Utf8.GetBytes(signedPayload));

        var result = CryptographicOperations.FixedTimeEquals(
            Utf8.GetBytes(Convert.ToHexString(hash).ToLowerInvariant()), Utf8.GetBytes(signature));

        logger.LogDebug("[Hookpipe.Validator:stripe-v1] {Result}", result ? "Passed" : "Failed: signature mismatch");
        return result;
    }

    private static bool TryParseStripeHeader(string header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = "";

        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;

            switch (kv[0].Trim())
            {
                case "t":
                    if (!long.TryParse(kv[1].Trim(), out timestamp))
                        return false;
                    break;
                case "v1":
                    signature = kv[1].Trim();
                    break;
            }
        }

        return timestamp > 0 && !string.IsNullOrEmpty(signature);
    }
}
