using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Polly;
using Runax.Hookpipe.Config;
using Runax.Hookpipe.Services;
using Runax.Hookpipe.Sinks;
using Runax.Hookpipe.Validation;

namespace Runax.Hookpipe.Hosting;

/// <summary>
/// Shared state behind the mapped endpoints: live config, sinks, retry pipelines, validators,
/// rate limiters, and the envelope builder. Registered as a singleton by
/// <see cref="HookpipeServiceCollectionExtensions.AddHookpipe"/>.
/// </summary>
internal sealed class HookpipeRuntime
{
    public HookpipeRuntime(HookpipeOptions options, ILoggerFactory loggerFactory)
    {
        var startupLogger = loggerFactory.CreateLogger("Runax.Hookpipe.Startup");

        ConfigProvider = new ConfigProvider(options.ConfigPath, loggerFactory.CreateLogger<ConfigProvider>());

        startupLogger.LogInformation(
            "[Hookpipe.Config] Loaded {EndpointCount} endpoint(s) and {SinkCount} sink(s) from '{Path}'",
            ConfigProvider.Current.Endpoints.Count, ConfigProvider.Current.Sinks.Count, options.ConfigPath);

        RetryPipelines = SinkFactory.CreateRetryPipelines(ConfigProvider.Current, loggerFactory);
        Validators = ValidatorFactory.CreateAll(loggerFactory);
        EnvelopeBuilder = new EnvelopeBuilder(loggerFactory.CreateLogger<EnvelopeBuilder>());
        RateLimiters = CreateRateLimiters(ConfigProvider.Current, startupLogger);
    }

    /// <summary>
    /// The live, hot-reloadable configuration.
    /// </summary>
    public ConfigProvider ConfigProvider { get; }

    /// <summary>
    /// Sinks keyed by ID. Empty until <see cref="HookpipeHostedService"/> connects them at startup,
    /// which happens before the server accepts requests; read-only after that.
    /// </summary>
    public Dictionary<string, ISink> Sinks { get; } = [];

    /// <summary>
    /// Retry pipelines keyed by sink ID, for sinks with a retry policy.
    /// </summary>
    public Dictionary<string, ResiliencePipeline> RetryPipelines { get; }

    /// <summary>
    /// Validators keyed by type.
    /// </summary>
    public Dictionary<string, IValidator> Validators { get; }

    /// <summary>
    /// Rate limiters keyed by endpoint ID, for endpoints with a rate limit.
    /// </summary>
    public Dictionary<string, RateLimiter> RateLimiters { get; }

    /// <summary>
    /// Builds the message envelope for each request.
    /// </summary>
    public EnvelopeBuilder EnvelopeBuilder { get; }

    private static Dictionary<string, RateLimiter> CreateRateLimiters(HookpipeConfig config, ILogger logger)
    {
        var rateLimiters = new Dictionary<string, RateLimiter>();
        foreach (var endpoint in config.Endpoints)
        {
            if (endpoint.RateLimit is null) continue;

            rateLimiters[endpoint.Id] = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = endpoint.RateLimit.Requests,
                Window = TimeSpan.FromSeconds(endpoint.RateLimit.WindowSeconds),
                QueueLimit = 0,
            });

            logger.LogInformation(
                "[Hookpipe.Endpoint:{Id}] Rate limit: {Requests} requests per {Window}s",
                endpoint.Id, endpoint.RateLimit.Requests, endpoint.RateLimit.WindowSeconds);
        }

        return rateLimiters;
    }
}
