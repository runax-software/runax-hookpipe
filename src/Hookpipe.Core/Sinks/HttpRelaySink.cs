using System.Text;
using System.Text.Json;
using Hookpipe.Core.Config;
using Hookpipe.Core.Helpers;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that forwards message envelopes as JSON POST requests to an HTTP endpoint.
/// Useful for relaying webhooks to another service or URL.
/// Settings: url_env, timeout_seconds (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class HttpRelaySink : ISink, ISinkHealthCheck, IDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "http";

    private readonly ILogger<HttpRelaySink> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _url;
    private readonly string _sinkId;

    private HttpRelaySink(ILogger<HttpRelaySink> logger, HttpClient httpClient, string url, string sinkId)
    {
        _logger = logger;
        _httpClient = httpClient;
        _url = url;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new HTTP relay sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing URL and timeout settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="HttpRelaySink"/> ready to forward messages.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URL env var is not set.</exception>
    public static HttpRelaySink Create(SinkConfig sinkConfig, ILogger<HttpRelaySink> logger)
    {
        var url = SinkHelper.RequireEnvVar(sinkConfig, "url_env", "HTTP_RELAY_URL");
        var timeoutSeconds = int.TryParse(
            sinkConfig.Settings.GetValueOrDefault("timeout_seconds", "30"), out var t)
            ? t
            : 30;

        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        logger.LogInformation(
            "[Hookpipe.Sink:http:{SinkId}] Configured to relay to {Url}, timeout={Timeout}s",
            sinkConfig.Id, LogHelper.MaskUri(url), timeoutSeconds);

        return new HttpRelaySink(logger, httpClient, url, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(message, SinkHelper.JsonOptions), Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(_url, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "[Hookpipe.Sink:http:{SinkId}] Relayed message '{MessageId}' to {Url}, status={StatusCode}",
                _sinkId, message.Id, LogHelper.MaskUri(_url), (int)response.StatusCode);
        }
        else
        {
            _logger.LogWarning(
                "[Hookpipe.Sink:http:{SinkId}] Relay failed for message '{MessageId}' to {Url}, status={StatusCode}",
                _sinkId, message.Id, LogHelper.MaskUri(_url), (int)response.StatusCode);

            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Probes reachability with a HEAD request. Any HTTP response (even 4xx/5xx) means the
    /// target is reachable and is treated as healthy; only transport-level failures are unhealthy.
    /// </remarks>
    public async Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, _url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return new SinkHealth(SinkHealthStatus.Healthy, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
