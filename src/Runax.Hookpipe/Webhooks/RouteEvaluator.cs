using Microsoft.Extensions.Logging;
using Runax.Hookpipe.Conditions;
using Runax.Hookpipe.Configuration;

namespace Runax.Hookpipe.Webhooks;

/// <summary>
/// Picks a request's targets: the webhook's <c>to</c>, or the first <c>route</c> rule whose
/// condition matches.
/// </summary>
internal static class RouteEvaluator
{
    /// <summary>
    /// Returns the targets for one request.
    /// </summary>
    /// <param name="context">What the conditions read from the request.</param>
    /// <param name="webhook">The webhook's live configuration.</param>
    /// <param name="logger">Logs which rule matched, at debug level.</param>
    public static IReadOnlyList<Target> Resolve(EvaluationContext context, WebhookConfiguration webhook, ILogger logger)
    {
        if (webhook.Route is null) return webhook.To ?? [];

        for (var index = 0; index < webhook.Route.Count; index++)
        {
            var rule = webhook.Route[index];
            if (rule.When is not null && !rule.When.Evaluate(context)) continue;

            logger.LogDebug("[Hookpipe.Webhook:{WebhookId}] Route rule {Index} matched -> [{Targets}]",
                context.WebhookId, index, string.Join(", ", rule.To));
            return rule.To;
        }

        return [];
    }
}
