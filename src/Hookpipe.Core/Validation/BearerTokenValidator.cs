using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates requests by computing the Authorization header's bearer token
/// against a secret stored in an environment variable.
/// </summary>
public sealed class BearerTokenValidator : IValidator
{
    /// <inheritdoc />
    public string Type => "bearer";

    /// <inheritdoc />
    public Task<bool> ValidateAsync(HttpContext context, ValidationConfig config)
    {
        if (config.Auth is null) return Task.FromResult(false);

        var expected = Environment.GetEnvironmentVariable(config.Auth.TokenEnv);
        if (string.IsNullOrEmpty(expected)) return Task.FromResult(false);

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            header = header["Bearer ".Length..].Trim();

        return Task.FromResult(header.Equals(expected, StringComparison.Ordinal));
    }
}
