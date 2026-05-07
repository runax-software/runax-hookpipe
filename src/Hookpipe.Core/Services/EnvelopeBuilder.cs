using System.Text.Json;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Services;

/// <summary>
/// Builds a <see cref="MessageEnvelope"/> from an incoming HTTP request
/// based on the matched endpoint configuration.
/// </summary>
public static class EnvelopeBuilder
{
    /// <summary>
    /// Creates a message envelope from the HTTP context and endpoint config.
    /// </summary>
    /// <param name="context">The incoming HTTP request context.</param>
    /// <param name="endpoint">The matched endpoint configuration.</param>
    /// <param name="pathParams">Optional path parameters extracted from the URL (e.g. {source} → "github").</param>
    /// <returns>A populated <see cref="MessageEnvelope"/> ready to be sent to a sink.</returns>
    public static async Task<MessageEnvelope> BuildAsync(
        HttpContext context,
        EndpointConfig endpoint,
        Dictionary<string, string>? pathParams = null)
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = endpoint.Id,
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        if (endpoint.Message.IncludeHeaders)
        {
            var filter = endpoint.Message.HeaderFilter;

            foreach (var header in context.Request.Headers)
                if (filter is null || filter.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                    envelope.Headers[header.Key] = header.Value.ToString();
        }

        if (endpoint.Message.IncludeBody)
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();

            try
            {
                envelope.Body = JsonSerializer.Deserialize<JsonElement>(raw);
            }
            catch
            {
                envelope.Body = raw;
            }
        }

        if (endpoint.Message.Metadata is null) return envelope;
        foreach (var (key, value) in endpoint.Message.Metadata)
        {
            var resolved = value;
            if (pathParams is not null)
                foreach (var (paramKey, paramValue) in pathParams)
                    resolved = resolved.Replace($"{{{paramKey}}}", paramValue);

            envelope.Metadata[key] = resolved;
        }

        return envelope;
    }
}