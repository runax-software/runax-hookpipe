using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Validation;
using Runax.Hookpipe.Webhooks;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Shared state behind the mapped webhooks: live configuration, the connected brokers, retry pipelines,
/// validators, rate limiters, and the envelope builder. Registered as a singleton by
/// <see cref="ServiceCollectionExtensions.AddRunaxHookpipe"/>.
/// </summary>
internal sealed class HookpipeRuntime
{
    public HookpipeRuntime(HookpipeOptions options, ILoggerFactory loggerFactory)
    {
        var startupLogger = loggerFactory.CreateLogger("Runax.Hookpipe.Startup");

        ConfigurationProvider = new HookpipeConfigurationProvider(options.ConfigurationPath, loggerFactory.CreateLogger<HookpipeConfigurationProvider>());

        startupLogger.LogInformation(
            "[Hookpipe.Configuration] Loaded {WebhookCount} webhook(s) and {BrokerCount} broker(s) from '{Path}'",
            ConfigurationProvider.Current.Webhooks.Count, ConfigurationProvider.Current.Brokers.Count, options.ConfigurationPath);

        RetryPipelines = new RetryPipelines(loggerFactory);
        Validators = ValidatorFactory.CreateAll(loggerFactory);
        EnvelopeBuilder = new EnvelopeBuilder(loggerFactory.CreateLogger<EnvelopeBuilder>());
        RateLimiters = CreateRateLimiters(ConfigurationProvider.Current, startupLogger);
    }

    /// <summary>
    /// The live, hot-reloadable configuration.
    /// </summary>
    public HookpipeConfigurationProvider ConfigurationProvider { get; }

    /// <summary>
    /// The connected brokers, keyed by broker ID. Empty until <see cref="HookpipeHostedService"/>
    /// connects them at startup, which happens before the server accepts requests; read-only after that.
    /// </summary>
    public Dictionary<string, IBroker> Brokers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retry pipelines for the brokers' retry policies.
    /// </summary>
    public RetryPipelines RetryPipelines { get; }

    /// <summary>
    /// Validators keyed by type.
    /// </summary>
    public Dictionary<string, IValidator> Validators { get; }

    /// <summary>
    /// Rate limiters keyed by webhook ID, for webhooks with a rate limit.
    /// </summary>
    public Dictionary<string, RateLimiter> RateLimiters { get; }

    /// <summary>
    /// Builds the message envelope for each request.
    /// </summary>
    public EnvelopeBuilder EnvelopeBuilder { get; }

    private static Dictionary<string, RateLimiter> CreateRateLimiters(HookpipeConfiguration configuration, ILogger logger)
    {
        var rateLimiters = new Dictionary<string, RateLimiter>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, webhook) in configuration.Webhooks)
        {
            if (webhook.RateLimit is null) continue;

            rateLimiters[id] = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = webhook.RateLimit.Requests,
                Window = webhook.RateLimit.Window,
                QueueLimit = 0,
            });

            logger.LogInformation(
                "[Hookpipe.Webhook:{Id}] Rate limit: {Requests} requests per {Window}",
                id, webhook.RateLimit.Requests, webhook.RateLimit.Window);
        }

        return rateLimiters;
    }
}
