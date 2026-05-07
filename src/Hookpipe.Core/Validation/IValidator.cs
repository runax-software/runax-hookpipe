using Hookpipe.Core.Config;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Validates incoming webhook requests before they are processed.
/// Each validation strategy (HMAC, bearer token, etc.) implements this interface.
/// </summary>
public interface IValidator
{
    /// <summary>
    /// Validator type identifier (e.g. "hmac-sha256", "bearer"). Used to match against config.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Validates the incoming request against the endpoint's validation config.
    /// </summary>
    /// <param name="context">The HTTP context of the incoming request.</param>
    /// <param name="config">The validation config for the matched endpoint.</param>
    /// <returns>True if the request is valid, false otherwise.</returns>
    Task<bool> ValidateAsync(HttpContext context, ValidationConfig config);
}
