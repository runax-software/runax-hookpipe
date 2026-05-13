using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Hookpipe.Core.Config;
using Hookpipe.Core.Metrics;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Prometheus;

namespace Hookpipe.Core.Services;

/// <summary>
/// Handles incoming webhook requests for a registered endpoint.
/// Performs method check, rate limiting, validation, envelope building, and sink production.
/// </summary>
public sealed class WebhookHandler(
    string endpointId,
    Regex regex,
    List<string> paramNames,
    ILogger logger,
    ConfigProvider configProvider,
    Dictionary<string, ISink> sinks,
    Dictionary<string, ResiliencePipeline> retryPipelines,
    Dictionary<string, IValidator> validators,
    Dictionary<string, RateLimiter> rateLimiters,
    EnvelopeBuilder envelopeBuilder)
{
    /// <summary>
    /// Handles an incoming HTTP request for this endpoint.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        using var timer = HookpipeMetrics.RequestDuration.WithLabels(endpointId).NewTimer();

        var liveEndpoint = ResolveEndpoint(context);
        if (liveEndpoint is null) return;

        if (!CheckMethod(context, liveEndpoint)) return;
        if (!await CheckRateLimit(context)) return;
        if (!await ValidateRequest(context, liveEndpoint)) return;

        try
        {
            var pathParams = ExtractPathParams(context);
            var envelope = await envelopeBuilder.BuildAsync(context, liveEndpoint, pathParams);
            await ProduceToSinks(context, envelope, liveEndpoint);

            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "202").Inc();
            context.Response.StatusCode = 202;

            await context.Response.WriteAsJsonAsync(new { status = "accepted", endpoint_id = endpointId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Hookpipe.Endpoint:{EndpointId}] Failed to process request", endpointId);

            HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "500").Inc();
            context.Response.StatusCode = 500;

            await context.Response.WriteAsJsonAsync(new { error = "internal_error", endpoint_id = endpointId });
        }
    }

    /// <summary>
    /// Resolves the live endpoint config. Returns null and sets 404 if removed.
    /// </summary>
    private EndpointConfig? ResolveEndpoint(HttpContext context)
    {
        var liveEndpoint = configProvider.Current.Endpoints.FirstOrDefault(e => e.Id == endpointId);
        if (liveEndpoint is not null) return liveEndpoint;

        logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Endpoint removed from config, returning 404", endpointId);

        HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "404").Inc();
        context.Response.StatusCode = 404;

        return null;
    }

    /// <summary>
    /// Checks if the HTTP method is allowed. Returns false and sets 405 if not.
    /// </summary>
    private bool CheckMethod(HttpContext context, EndpointConfig endpoint)
    {
        var methods = endpoint.Methods.Select(m => m.ToUpperInvariant()).ToHashSet();
        if (methods.Contains(context.Request.Method)) return true;

        logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Method not allowed: {Method}", endpointId,
            context.Request.Method);

        HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "405").Inc();
        context.Response.StatusCode = 405;

        return false;
    }

    /// <summary>
    /// Checks rate limit. Returns false and sets 429 if exceeded.
    /// </summary>
    private async Task<bool> CheckRateLimit(HttpContext context)
    {
        if (!rateLimiters.TryGetValue(endpointId, out var limiter)) return true;

        using var lease = await limiter.AcquireAsync(1, context.RequestAborted);
        if (lease.IsAcquired) return true;

        logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Rate limit exceeded", endpointId);

        HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "429").Inc();
        context.Response.StatusCode = 429;

        await context.Response.WriteAsJsonAsync(new { error = "rate_limit_exceeded", endpoint_id = endpointId });

        return false;
    }

    /// <summary>
    /// Validates the request. Returns false and sets 401 if validation fails.
    /// </summary>
    private async Task<bool> ValidateRequest(HttpContext context, EndpointConfig endpoint)
    {
        if (endpoint.Validation is null) return true;

        IValidator? validator = null;

        if (endpoint.Validation.Auth is not null)
            validators.TryGetValue(endpoint.Validation.Auth.Type, out validator);
        else if (endpoint.Validation.Signature is not null)
            validators.TryGetValue(endpoint.Validation.Signature.Algorithm, out validator);

        if (validator is not null && await validator.ValidateAsync(context, endpoint.Validation))
        {
            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation passed ({ValidatorType})",
                endpointId, validator.Type);
            return true;
        }

        logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Validation failed, returning 401", endpointId);

        HookpipeMetrics.ValidationFailuresTotal.WithLabels(endpointId, validator?.Type ?? "unknown").Inc();
        HookpipeMetrics.RequestsTotal.WithLabels(endpointId, context.Request.Method, "401").Inc();

        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });

        return false;
    }

    /// <summary>
    /// Extracts path parameters from the request URL. Returns null if no params.
    /// </summary>
    private Dictionary<string, string>? ExtractPathParams(HttpContext context)
    {
        var match = regex.Match(context.Request.Path.Value ?? "");
        if (!match.Success || paramNames.Count == 0) return null;

        var pathParams = new Dictionary<string, string>();
        for (var i = 0; i < paramNames.Count; i++)
            pathParams[paramNames[i]] = match.Groups[i + 1].Value;

        logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Extracted {Count} path param(s)",
            endpointId, pathParams.Count);
        return pathParams;
    }

    /// <summary>
    /// Produces the envelope to all resolved sinks with retry support.
    /// </summary>
    private async Task ProduceToSinks(HttpContext context, MessageEnvelope envelope, EndpointConfig endpoint)
    {
        foreach (var sinkId in endpoint.GetResolvedSinks())
        {
            if (!sinks.TryGetValue(sinkId, out var sink))
            {
                logger.LogError("[Hookpipe.Endpoint:{EndpointId}] Sink '{SinkId}' not found", endpointId, sinkId);
                HookpipeMetrics.SinkErrorsTotal.WithLabels(endpointId, sinkId).Inc();
                continue;
            }

            try
            {
                if (retryPipelines.TryGetValue(sinkId, out var retryPipeline))
                {
                    await retryPipeline.ExecuteAsync(
                        static (state, cancellationToken) =>
                            new ValueTask(state.sink.ProduceAsync(state.envelope, cancellationToken)),
                        (sink, envelope),
                        context.RequestAborted);
                }
                else await sink.ProduceAsync(envelope, context.RequestAborted);
            }
            catch (Exception ex)
            {
                HookpipeMetrics.SinkErrorsTotal.WithLabels(endpointId, sinkId).Inc();
                logger.LogError(ex, "[Hookpipe.Endpoint:{EndpointId}] Sink '{SinkId}' failed to produce",
                    endpointId, sinkId);
                throw;
            }

            logger.LogDebug("[Hookpipe.Endpoint:{EndpointId}] Message '{MessageId}' produced to sink '{SinkId}'",
                endpointId, envelope.Id, sinkId);
            HookpipeMetrics.MessagesProducedTotal.WithLabels(endpointId, sinkId).Inc();
        }
    }
}
