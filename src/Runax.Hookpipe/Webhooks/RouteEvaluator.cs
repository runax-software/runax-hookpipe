using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe.Webhooks;

/// <summary>
/// Picks a request's targets: the webhook's <c>to</c>, or the first <c>route</c> rule whose
/// condition matches. The body is parsed as JSON once, and only when a rule reads it.
/// </summary>
internal static class RouteEvaluator
{
    /// <summary>
    /// Returns the targets for one request.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="webhookId">The webhook's ID, for logging.</param>
    /// <param name="webhook">The webhook's live configuration.</param>
    /// <param name="pathParameters">The route's path parameters.</param>
    /// <param name="logger">Logs which rule matched, at debug level.</param>
    public static async Task<IReadOnlyList<Target>> ResolveAsync(
        HttpContext context,
        string webhookId,
        WebhookConfiguration webhook,
        IReadOnlyDictionary<string, string> pathParameters,
        ILogger logger)
    {
        if (webhook.Route is null) return webhook.To ?? [];

        var conditionContext = await CreateContextAsync(context, webhookId, webhook.Route, pathParameters, logger);

        for (var index = 0; index < webhook.Route.Count; index++)
        {
            var rule = webhook.Route[index];
            if (rule.When is not null && !rule.When.Evaluate(conditionContext)) continue;

            logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Route rule {Index} matched -> [{Targets}]",
                webhookId, index, string.Join(", ", rule.To));
            return rule.To;
        }

        return [];
    }

    private static async Task<ConditionContext> CreateContextAsync(
        HttpContext context,
        string webhookId,
        IReadOnlyList<RouteRule> route,
        IReadOnlyDictionary<string, string> pathParameters,
        ILogger logger)
    {
        var headers = context.Request.Headers;
        string? GetHeader(string name) => headers.TryGetValue(name, out var value) ? value.ToString() : null;

        if (!route.Any(rule => rule.When?.UsesJsonPath == true))
            return new ConditionContext(GetHeader, pathParameters, body: null, bodyIsJson: false);

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        try
        {
            return new ConditionContext(GetHeader, pathParameters, JsonNode.Parse(raw), bodyIsJson: true);
        }
        catch (JsonException)
        {
            logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Body is not JSON, so 'jsonpath' conditions won't match",
                webhookId);
            return new ConditionContext(GetHeader, pathParameters, body: null, bodyIsJson: false);
        }
    }
}
