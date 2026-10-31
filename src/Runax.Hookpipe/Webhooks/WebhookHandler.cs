using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Diagnostics;
using Runax.Hookpipe.Hosting;
using Runax.Hookpipe.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Webhooks;

/// <summary>
/// Handles requests for one webhook: method check, rate limit, validation, routing, envelope
/// building, and publishing to every target. Reads the webhook's live configuration on each request.
/// Metadata and topic templates are rendered per message; a target whose topic template fails is
/// skipped and counted, without affecting the other targets.
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
        if (!await ApplyBodySizeLimitAsync(context, webhook)) return;
        if (!await CheckRateLimit(context)) return;
        if (!await ValidateRequest(context, webhook)) return;

        try
        {
            var pathParameters = context.Request.RouteValues
                .Where(value => value.Value is not null)
                .ToDictionary(value => value.Key, value => value.Value!.ToString()!, StringComparer.OrdinalIgnoreCase);

            var evaluation = await CreateEvaluationContextAsync(context, webhook, pathParameters);
            var targets = RouteEvaluator.Resolve(evaluation, webhook, logger);

            if (!TryRenderMetadata(webhook, evaluation, out var metadata) && targets.Count > 0)
            {
                foreach (var target in targets)
                    HookpipeMetrics.RecordTemplateFailure(target.Broker, "template_unresolved");

                await RespondPublishFailedAsync(context);
                return;
            }

            evaluation.Metadata = metadata;
            var envelope = await runtime.EnvelopeBuilder.BuildAsync(context, webhookId, webhook, metadata);
            var successCount = await PublishAsync(context, envelope, targets, evaluation);

            if (targets.Count == 0 || successCount > 0)
            {
                HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "202");
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.WriteAsJsonAsync(
                    new { status = "accepted", webhook_id = webhookId, message_id = envelope.Id });
            }
            else
            {
                await RespondPublishFailedAsync(context);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The server enforced the limit while the body was being read, so its length wasn't known up front.
            await RespondTooLargeAsync(context, webhook.MaxBodySize);
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
    /// Applies the webhook's <c>max_body_size</c> to this request, read from the live configuration so
    /// a reload changes it. Returns false, after answering 413, when the request declares a larger
    /// body. The limit is set before anything reads the body, because
    /// <see cref="IHttpMaxRequestBodySizeFeature.MaxRequestBodySize"/> can't be changed after that.
    /// </summary>
    private async Task<bool> ApplyBodySizeLimitAsync(HttpContext context, WebhookConfiguration webhook)
    {
        if (webhook.MaxBodySize is not { } limit) return true;

        // The feature is missing on servers that don't support a per-request limit, such as TestServer,
        // and read-only once the body has been read. Either way, Content-Length still answers 413.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            feature.MaxRequestBodySize = limit;

        if (context.Request.ContentLength is not { } length || length <= limit) return true;

        await RespondTooLargeAsync(context, limit);
        return false;
    }

    private async Task RespondTooLargeAsync(HttpContext context, long? limit)
    {
        logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Body over max_body_size ({Limit} bytes), returning 413",
            webhookId, limit);
        HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "413");
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new { error = "payload_too_large", webhook_id = webhookId });
    }

    private async Task RespondPublishFailedAsync(HttpContext context)
    {
        HookpipeMetrics.RecordRequest(webhookId, context.Request.Method, "500");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "publish_failed", webhook_id = webhookId });
    }

    /// <summary>
    /// Collects what conditions and templates read. The body is read only when one of them uses
    /// JSONPath, and parsed on first use.
    /// </summary>
    private async Task<EvaluationContext> CreateEvaluationContextAsync(
        HttpContext context,
        WebhookConfiguration webhook,
        IReadOnlyDictionary<string, string> pathParameters)
    {
        var headers = context.Request.Headers;
        string? GetHeader(string name) => headers.TryGetValue(name, out var value) ? value.ToString() : null;

        string? rawBody = null;
        if (UsesJsonPath(webhook))
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            rawBody = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        return new EvaluationContext(webhookId, GetHeader, pathParameters, rawBody, () =>
            logger.LogDebug(
                "[Hookpipe.Webhook:{WebhookId}] Body is not JSON, so JSONPath conditions and placeholders select nothing",
                webhookId));
    }

    private static bool UsesJsonPath(WebhookConfiguration webhook) =>
        (webhook.Route ?? []).Any(rule => rule.When?.UsesJsonPath == true) ||
        (webhook.To ?? []).Concat((webhook.Route ?? []).SelectMany(rule => rule.To))
        .Any(target => target.TopicTemplate?.UsesJsonPath == true) ||
        webhook.Message.Metadata.Keys.Any(key => webhook.Message.MetadataTemplate(key).UsesJsonPath);

    /// <summary>
    /// Renders every <c>message.metadata</c> value. Returns false, after logging the first placeholder
    /// that had no value, when one didn't render.
    /// </summary>
    private bool TryRenderMetadata(
        WebhookConfiguration webhook,
        EvaluationContext evaluation,
        out Dictionary<string, string> metadata)
    {
        metadata = new Dictionary<string, string>();

        foreach (var key in webhook.Message.Metadata.Keys)
        {
            if (webhook.Message.MetadataTemplate(key).TryRender(evaluation, out var value, out var unresolved))
            {
                metadata[key] = value;
                continue;
            }

            logger.LogWarning(
                "[Hookpipe.Webhook:{WebhookId}] Not publishing: metadata '{Key}' placeholder '{Placeholder}' has no value",
                webhookId, key, unresolved!.Text);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Renders a target's topic when it's a template. Returns false, after logging and counting the
    /// failure, when a placeholder has no value or the rendered topic isn't allowed.
    /// </summary>
    private bool TryRenderTopic(Target target, EvaluationContext evaluation, MessageEnvelope envelope, out string? topic)
    {
        topic = target.Topic;
        if (target.TopicTemplate is not { } template) return true;

        if (!template.TryRender(evaluation, out var rendered, out var unresolved))
        {
            logger.LogWarning(
                "[Hookpipe.Webhook:{WebhookId}] Message '{MessageId}' not published to '{Target}': " +
                "placeholder '{Placeholder}' has no value", webhookId, envelope.Id, target, unresolved!.Text);
            HookpipeMetrics.RecordTemplateFailure(target.Broker, "template_unresolved");
            return false;
        }

        if (!Target.IsSafeTopic(rendered))
        {
            logger.LogWarning(
                "[Hookpipe.Webhook:{WebhookId}] Message '{MessageId}' not published to '{Target}': the rendered topic " +
                "may only use letters, digits, '.', '_', and '-'", webhookId, envelope.Id, target);
            HookpipeMetrics.RecordTemplateFailure(target.Broker, "template_invalid");
            return false;
        }

        topic = rendered;
        return true;
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
    private async Task<int> PublishAsync(
        HttpContext context,
        MessageEnvelope envelope,
        IReadOnlyList<Target> targets,
        EvaluationContext evaluation)
    {
        var successCount = 0;
        var brokers = runtime.ConfigurationProvider.Current.Brokers;

        await Task.WhenAll(targets.Select(async target =>
        {
            if (!TryRenderTopic(target, evaluation, envelope, out var topic)) return;

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
                        (broker, Topic: topic, envelope),
                        context.RequestAborted);
                }
                else await broker.PublishAsync(topic, envelope, context.RequestAborted);

                Interlocked.Increment(ref successCount);
                logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Message '{MessageId}' published to '{Broker}:{Topic}'",
                    webhookId, envelope.Id, target.Broker, topic);
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
