using Runax.Hookpipe.Configuration;
using Microsoft.AspNetCore.Http;

namespace Runax.Hookpipe.Validation;

/// <summary>
/// Validates incoming webhook requests before they are processed.
/// Each validation strategy (HMAC, bearer token, etc.) implements this interface.
/// </summary>
public interface IValidator
{
    /// <summary>
    /// Validator type identifier (e.g. "hmac-sha256", "bearer"). Used to match against configuration.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Validates the incoming request against one entry of the webhook's <c>validate</c> list.
    /// </summary>
    /// <param name="context">The HTTP context of the incoming request.</param>
    /// <param name="configuration">The validator entry, with its secrets already resolved.</param>
    /// <returns>True if the request is valid, false otherwise.</returns>
    Task<bool> ValidateAsync(HttpContext context, ValidatorConfiguration configuration);
}
