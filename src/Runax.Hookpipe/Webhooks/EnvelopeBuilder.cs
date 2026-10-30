using System.Text.Json;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Webhooks;

/// <summary>
/// Builds a <see cref="MessageEnvelope"/> from an incoming HTTP request
/// based on the matched webhook configuration.
/// Parses JSON bodies when possible, falls back to raw string.
/// </summary>
public sealed class EnvelopeBuilder(ILogger<EnvelopeBuilder> logger)
{
    /// <summary>
    /// Creates a message envelope from the HTTP context and webhook configuration.
    /// </summary>
    /// <param name="context">The incoming HTTP request context.</param>
    /// <param name="webhookId">The ID of the webhook that received the request.</param>
    /// <param name="webhook">The webhook's configuration.</param>
    /// <param name="metadata">The webhook's <c>message.metadata</c>, with its templates already rendered.</param>
    /// <returns>A populated <see cref="MessageEnvelope"/> ready to be published.</returns>
    public async Task<MessageEnvelope> BuildAsync(
        HttpContext context,
        string webhookId,
        WebhookConfiguration webhook,
        IReadOnlyDictionary<string, string> metadata)
    {
        logger.LogDebug("[Hookpipe.Envelope] Building for webhook '{WebhookId}', {Method} {Path}",
            webhookId, context.Request.Method, context.Request.Path.Value);

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = webhookId,
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        if (webhook.Message.IncludeHeaders)
        {
            var names = webhook.Message.HeaderNames;

            foreach (var header in context.Request.Headers)
                if (names is null || names.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    envelope.Headers[header.Key] = header.Value.ToString();

            logger.LogDebug("[Hookpipe.Envelope] Included {Count} header(s)", envelope.Headers.Count);
        }

        if (webhook.Message.Body)
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var raw = await reader.ReadToEndAsync(context.RequestAborted);

            try
            {
                envelope.Body = JsonSerializer.Deserialize<JsonElement>(raw);
                logger.LogDebug("[Hookpipe.Envelope] Parsed body as JSON");
            }
            catch (JsonException)
            {
                logger.LogDebug("[Hookpipe.Envelope] Body is not JSON on webhook '{WebhookId}', using raw string",
                    webhookId);
                envelope.Body = raw;
            }
        }

        if (metadata.Count == 0) return envelope;
        foreach (var (key, value) in metadata)
            envelope.Metadata[key] = value;

        logger.LogDebug("[Hookpipe.Envelope] Added {Count} metadata key(s)", envelope.Metadata.Count);

        return envelope;
    }
}
