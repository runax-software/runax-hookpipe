using System.Security.Cryptography;
using System.Text;
using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates requests by computing an HMAC-SHA256 signature of the request body
/// and comparing it to the value in the configured header.
/// Handles signatures with or without an algorithm prefix (e.g. GitHub's "sha256=&lt;hex&gt;").
/// </summary>
public sealed class HmacSha256Validator : IValidator
{
    /// <inheritdoc />
    public string Type => "hmac-sha256";

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Signature is null) return false;

        var secret = Environment.GetEnvironmentVariable(config.Signature.SecretEnv);
        if (string.IsNullOrEmpty(secret)) return false;

        if (!context.Request.Headers.TryGetValue(config.Signature.Header, out var signatureHeader)) return false;
        var signature = signatureHeader.ToString();

        var prefixIndex = signature.IndexOf('=');
        if (prefixIndex >= 0) signature = signature[(prefixIndex + 1)..];

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var encoding = Encoding.UTF8;
        using var hmac = new HMACSHA256(encoding.GetBytes(secret));
        var hash = hmac.ComputeHash(encoding.GetBytes(body));

        return CryptographicOperations.FixedTimeEquals(
            encoding.GetBytes(Convert.ToHexString(hash).ToLowerInvariant()),
            encoding.GetBytes(signature));
    }
}
