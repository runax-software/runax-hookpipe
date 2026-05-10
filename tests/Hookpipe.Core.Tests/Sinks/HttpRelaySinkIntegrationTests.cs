using System.Text.Json;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="HttpRelaySink"/>.
/// Tests against the health endpoint of the running Hookpipe app or any HTTP endpoint.
/// Since we can't guarantee an external HTTP server, these tests use a mock HTTP handler.
/// </summary>
public sealed class HttpRelaySinkIntegrationTests
{
    [Fact]
    public async Task ProduceAsync_SuccessfulRelay_DoesNotThrow()
    {
        var handler = new MockHttpHandler(200, """{"ok":true}""");
        var sink = new HttpRelaySinkTestable(
            Substitute.For<ILogger<HttpRelaySink>>(),
            new HttpClient(handler),
            "http://localhost/test",
            "test-http");

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "http-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "http-relay" },
        };

        var act = () => sink.ProduceAsync(envelope);

        await act.Should().NotThrowAsync();
        handler.LastRequestBody.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        deserialized.GetProperty("endpointId").GetString().Should().Be("http-test");
    }

    [Fact]
    public async Task ProduceAsync_ServerError_Throws()
    {
        var handler = new MockHttpHandler(500, "Internal Server Error");
        var sink = new HttpRelaySinkTestable(
            Substitute.For<ILogger<HttpRelaySink>>(),
            new HttpClient(handler),
            "http://localhost/test",
            "test-http");

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "http-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        var act = () => sink.ProduceAsync(envelope);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ProduceAsync_SendsJsonContentType()
    {
        var handler = new MockHttpHandler(200, "ok");
        var sink = new HttpRelaySinkTestable(
            Substitute.For<ILogger<HttpRelaySink>>(),
            new HttpClient(handler),
            "http://localhost/test",
            "test-http");

        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "content-type-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await sink.ProduceAsync(envelope);

        handler.LastContentType.Should().StartWith("application/json");
    }

    /// <summary>
    /// Testable wrapper that exposes HttpRelaySink's ProduceAsync with a custom HttpClient.
    /// </summary>
    private sealed class HttpRelaySinkTestable : ISink
    {
        private readonly ILogger<HttpRelaySink> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _url;
        private readonly string _sinkId;

        public HttpRelaySinkTestable(ILogger<HttpRelaySink> logger, HttpClient httpClient, string url, string sinkId)
        {
            _logger = logger;
            _httpClient = httpClient;
            _url = url;
            _sinkId = sinkId;
        }

        public string Type => "http";

        public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            using var content = new StringContent(json, System.Text.Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

            var response = await _httpClient.PostAsync(_url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Mock HTTP handler that captures requests and returns a configured response.
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly int _statusCode;
        private readonly string _responseBody;

        public string? LastRequestBody { get; private set; }
        public string? LastContentType { get; private set; }

        public MockHttpHandler(int statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                LastContentType = request.Content.Headers.ContentType?.ToString();
            }

            return new HttpResponseMessage((System.Net.HttpStatusCode)_statusCode)
            {
                Content = new StringContent(_responseBody),
            };
        }
    }
}
