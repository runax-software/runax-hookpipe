using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Diagnostics;
using Runax.Hookpipe.Hosting;
using Runax.Hookpipe.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Webhooks;

/// <summary>
/// Handles requests for one webhook: method check, rate limit, validation, routing, envelope
/// building, and publishing to every target. Reads the webhook's live configuration on each request.
/// </summary>
internal sealed class WebhookHandler(string webhookId, ILogger logger, HookpipeRuntime runtime)
{
    /// <summary>
    /// Handles an incoming HTTP request for this webhook.
    /// </summary>
    public async Task HandleAsync(HttpContext context)
    {
        using var timer = HookpipeMetrics.TimeRequest(webhookId);

        var webhook = ResolveWebhook(context);
        if (webhook is null) return;

        if (!CheckMethod(context, webhook)) return;
        if (!await CheckRateLimit(context)) return;
        if (!await ValidateRequest(context, webhook)) return;

        try
        {
            var pathParameters = context.Request.RouteValues
                .Where(value => value.Value is not null)
                .ToDictionary(value => value.Key, value => value.Value!.ToString()!, StringComparer.OrdinalIgnoreCase);

            var targets = await RouteEvaluator.ResolveAsync(context, webhookId, webhook, pathParameters, logger);
            var envelope = await runtime.EnvelopeBuilder.BuildAsync(context, webhookId, webhook, pathParameters);
            var successCount = await PublishAsync(context, envelope, targets);

            if (targets.Count == 0 || successCount > 0)
            {
                HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "202");
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.WriteAsJsonAsync(
                    new { status = "accepted", webhook_id = webhookId, message_id = envelope.Id });
            }
            else
            {
                HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "500");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "publish_failed", webhook_id = webhookId });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Hookpipe.Webhook:{WebhookId}] Failed to process request", webhookId);
            HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "500");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "internal_error", webhook_id = webhookId });
        }
    }

    /// <summary>
    /// Resolves the live webhook configuration. Returns null and sets 404 if it was removed by a reload.
    /// </summary>
    private WebhookConfiguration? ResolveWebhook(HttpContext context)
    {
        if (runtime.ConfigurationProvider.Current.Webhooks.TryGetValue(webhookId, out var webhook)) return webhook;

        logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Webhook removed from config, returning 404", webhookId);
        HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "404");
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return null;
    }

    /// <summary>
    /// Checks if the HTTP method is allowed. Returns false and sets 405 if not.
    /// </summary>
    private bool CheckMethod(HttpContext context, WebhookConfiguration webhook)
    {
        if (webhook.Methods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)) return true;

        logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Method not allowed: {Method}",
            webhookId, context.Request.Method);
        HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "405");
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return false;
    }

    /// <summary>
    /// Checks rate limit. Returns false and sets 429 if exceeded.
    /// </summary>
    private async Task<bool> CheckRateLimit(HttpContext context)
    {
        if (!runtime.RateLimiters.TryGetValue(webhookId, out var limiter)) return true;

        using var lease = await limiter.AcquireAsync(1, context.RequestAborted);
        if (lease.IsAcquired) return true;

        logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Rate limit exceeded", webhookId);
        HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "429");
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new { error = "rate_limit_exceeded", webhook_id = webhookId });
        return false;
    }

    /// <summary>
    /// Runs every validator in order. Returns false and sets 401 as soon as one fails.
    /// </summary>
    private async Task<bool> ValidateRequest(HttpContext context, WebhookConfiguration webhook)
    {
        foreach (var configuration in webhook.Validate)
        {
            if (runtime.Validators.TryGetValue(configuration.Type, out var validator) &&
                await validator.ValidateAsync(context, configuration))
            {
                logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Validation passed ({ValidatorType})",
                    webhookId, configuration.Type);
                continue;
            }

            logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Validation failed ({ValidatorType}), returning 401",
                webhookId, configuration.Type);
            HookpipeMetrics.RecordValidationFailure(webhookId, configuration.Type);
            HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "401");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return false;
        }

        return true;
    }

    /// <summary>
    /// Publishes the envelope to every target in parallel, retrying per the broker's policy.
    /// Returns the number of targets that succeeded.
    /// </summary>
    private async Task<int> PublishAsync(HttpContext context, MessageEnvelope envelope, IReadOnlyList<Target> targets)
    {
        var successCount = 0;
        var brokers = runtime.ConfigurationProvider.Current.Brokers;

        await Task.WhenAll(targets.Select(async target =>
        {
            if (!runtime.Brokers.TryGetValue(target.Broker, out var broker))
            {
                logger.LogError("[Hookpipe.Webhook:{WebhookId}] Broker '{BrokerId}' isn't running", webhookId,
                    target.Broker);
                HookpipeMetrics.RecordPublishError(webhookId, target.Broker);
                return;
            }

            try
            {
                var retry = runtime.RetryPipelines.Get(target.Broker, brokers.GetValueOrDefault(target.Broker)?.Publish.Retry);
                if (retry is not null)
                {
                    await retry.ExecuteAsync(
                        static (state, cancellationToken) =>
                            new ValueTask(state.broker.PublishAsync(state.Topic, state.envelope, cancellationToken)),
                        (broker, target.Topic, envelope),
                        context.RequestAborted);
                }
                else await broker.PublishAsync(target.Topic, envelope, context.RequestAborted);

                Interlocked.Increment(ref successCount);
                logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Message '{MessageId}' published to '{Target}'",
                    webhookId, envelope.Id, target);
                HookpipeMetrics.RecordMessagePublished(webhookId, target.Broker);
            }
            catch (Exception ex)
            {
                HookpipeMetrics.RecordPublishError(webhookId, target.Broker);
                logger.LogError(ex, "[Hookpipe.Webhook:{WebhookId}] Publishing to '{Target}' failed", webhookId,
                    target);
            }
        }));

        return successCount;
    }
}
