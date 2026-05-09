using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Creates all available validators with logging support.
/// </summary>
public static class ValidatorFactory
{
    /// <summary>
    /// Returns a dictionary of all registered validators keyed by type string.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating validator-specific loggers.</param>
    /// <returns>Dictionary of validators keyed by type (e.g. "bearer", "hmac-sha256").</returns>
    public static Dictionary<string, IValidator> CreateAll(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(ValidatorFactory));

        var validators = new Dictionary<string, IValidator>
        {
            ["bearer"] = new BearerTokenValidator(loggerFactory.CreateLogger<BearerTokenValidator>()),
            ["hmac-sha256"] = new HmacSha256Validator(loggerFactory.CreateLogger<HmacSha256Validator>()),
        };

        logger.LogInformation("[Hookpipe.Validator] Registered {Count} validator(s): {Types}",
            validators.Count, string.Join(", ", validators.Keys));

        return validators;
    }
}
