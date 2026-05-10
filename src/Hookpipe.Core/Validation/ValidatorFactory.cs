using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Validation;

/// <summary>
/// Creates all available validators with logging support.
/// </summary>
public static class ValidatorFactory
{
    /// <summary>
    /// All known validator type identifiers. Used by <see cref="Config.ConfigLoader"/> for validation.
    /// </summary>
    public static readonly HashSet<string> KnownTypes =
    [
        BearerTokenValidator.TypeName,
        HmacSha256Validator.TypeName,
        ApiKeyValidator.TypeName,
        IpAllowlistValidator.TypeName,
        StripeSignatureValidator.TypeName,
    ];

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
            [BearerTokenValidator.TypeName] =
                new BearerTokenValidator(loggerFactory.CreateLogger<BearerTokenValidator>()),
            [HmacSha256Validator.TypeName] = new HmacSha256Validator(loggerFactory.CreateLogger<HmacSha256Validator>()),
            [ApiKeyValidator.TypeName] = new ApiKeyValidator(loggerFactory.CreateLogger<ApiKeyValidator>()),
            [IpAllowlistValidator.TypeName] =
                new IpAllowlistValidator(loggerFactory.CreateLogger<IpAllowlistValidator>()),
            [StripeSignatureValidator.TypeName] =
                new StripeSignatureValidator(loggerFactory.CreateLogger<StripeSignatureValidator>()),
        };

        logger.LogInformation("[Hookpipe.Validator] Registered {Count} validator(s): {Types}", validators.Count,
            string.Join(", ", validators.Keys));

        return validators;
    }
}
