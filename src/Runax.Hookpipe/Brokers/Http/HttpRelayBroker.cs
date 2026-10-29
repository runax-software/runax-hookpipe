using System.Text;
using System.Text.Json;
using Runax.Hookpipe.Helpers;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Health;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers.Http;

/// <summary>
/// Broker that POSTs message envelopes as JSON to an HTTP URL. A target's topic, when present, is
/// appended to the URL as a path: <c>relay:orders</c> POSTs to <c>{url}/orders</c>.
/// A non-success status code counts as a failed publish.
/// </summary>
public sealed class HttpRelayBroker : IBroker, IBrokerHealthCheck, IDisposable
{
    /// <summary>
    /// The broker type identifier.
    /// </summary>
    public const string TypeName = "http";

    private readonly ILogger<HttpRelayBroker> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _url;
    private readonly string _brokerId;

    private HttpRelayBroker(ILogger<HttpRelayBroker> logger, HttpClient httpClient, string url, string brokerId)
    {
        _logger = logger;
        _httpClient = httpClient;
        _url = url;
        _brokerId = brokerId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates the relay with its own <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="brokerId">The broker ID, for logging.</param>
    /// <param name="settings">The broker's settings.</param>
    /// <param name="logger">Logger for this broker.</param>
    /// <returns>A configured <see cref="HttpRelayBroker"/>.</returns>
    public static HttpRelayBroker Create(string brokerId, HttpBrokerSettings settings, ILogger<HttpRelayBroker> logger)
    {
        var httpClient = new HttpClient { Timeout = settings.Timeout };

        logger.LogInformation(
            "[Hookpipe.Broker:http:{BrokerId}] Relaying to {Url}, timeout={Timeout}s",
            brokerId, LogHelper.MaskUri(settings.Url), settings.Timeout.TotalSeconds);

        return new HttpRelayBroker(logger, httpClient, settings.Url, brokerId);
    }

    /// <inheritdoc />
    /// <exception cref="HttpRequestException">Thrown when the response status isn't a success.</exception>
    public async Task PublishAsync(string? topic, MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var url = UrlFor(_url, topic);
        using var content = new StringContent(JsonSerializer.Serialize(message, BrokerHelper.JsonOptions), Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "[Hookpipe.Broker:http:{BrokerId}] Relayed message '{MessageId}' to {Url}, status={StatusCode}",
                _brokerId, message.Id, LogHelper.MaskUri(url), (int)response.StatusCode);
        }
        else
        {
            _logger.LogWarning(
                "[Hookpipe.Broker:http:{BrokerId}] Relay failed for message '{MessageId}' to {Url}, status={StatusCode}",
                _brokerId, message.Id, LogHelper.MaskUri(url), (int)response.StatusCode);

            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public async Task<BrokerHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, _url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return new BrokerHealth(BrokerHealthStatus.Healthy, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new BrokerHealth(BrokerHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Appends a topic to the base URL as a path segment, keeping any query string.
    /// </summary>
    internal static string UrlFor(string baseUrl, string? topic)
    {
        if (string.IsNullOrEmpty(topic)) return baseUrl;

        var builder = new UriBuilder(baseUrl);
        builder.Path = builder.Path.TrimEnd('/') + "/" + Uri.EscapeDataString(topic);
        return builder.Uri.ToString();
    }
}
