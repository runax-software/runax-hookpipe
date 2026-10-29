using System.Text.Json;
using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Brokers.Http;

/// <summary>
/// Integration tests for <see cref="HttpRelayBroker"/>.
/// Relays to an in-process Kestrel server on a free loopback port, so no external service is needed.
/// </summary>
public sealed class HttpRelayBrokerIntegrationTests
{
    [Fact]
    public async Task ProduceAsync_SuccessfulRelay_PostsEnvelopeToBaseUrl()
    {
        await using var server = await RelayServer.StartAsync(StatusCodes.Status200OK);
        using var broker = CreateBroker(server.Url("/hooks"));

        var act = () => broker.PublishAsync(null, MakeEnvelope("http-test", new { test = "http-relay" }));

        await act.Should().NotThrowAsync();
        server.LastRequest.Should().NotBeNull();
        server.LastRequest!.Method.Should().Be("POST");
        server.LastRequest.Path.Should().Be("/hooks");

        var deserialized = JsonSerializer.Deserialize<JsonElement>(server.LastRequest.Body);
        deserialized.GetProperty("webhookId").GetString().Should().Be("http-test");
    }

    [Fact]
    public async Task ProduceAsync_WithTopic_PostsToTopicPath()
    {
        await using var server = await RelayServer.StartAsync(StatusCodes.Status202Accepted);
        using var broker = CreateBroker(server.Url("/hooks?token=abc"));

        await broker.PublishAsync("orders", MakeEnvelope("http-topic-test"));

        server.LastRequest.Should().NotBeNull();
        server.LastRequest!.Path.Should().Be("/hooks/orders");
        server.LastRequest.Query.Should().Be("?token=abc");
        JsonSerializer.Deserialize<JsonElement>(server.LastRequest.Body)
            .GetProperty("webhookId").GetString().Should().Be("http-topic-test");
    }

    [Fact]
    public async Task ProduceAsync_ServerError_Throws()
    {
        await using var server = await RelayServer.StartAsync(StatusCodes.Status500InternalServerError);
        using var broker = CreateBroker(server.Url("/hooks"));

        var act = () => broker.PublishAsync(null, MakeEnvelope("http-test"));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ProduceAsync_SendsJsonContentType()
    {
        await using var server = await RelayServer.StartAsync(StatusCodes.Status200OK);
        using var broker = CreateBroker(server.Url("/hooks"));

        await broker.PublishAsync(null, MakeEnvelope("content-type-test"));

        server.LastRequest.Should().NotBeNull();
        server.LastRequest!.ContentType.Should().StartWith("application/json");
    }

    private static HttpRelayBroker CreateBroker(string url) => HttpRelayBroker.Create(
        "test-http",
        new HttpBrokerSettings { Url = url, Timeout = TimeSpan.FromSeconds(10) },
        Substitute.For<ILogger<HttpRelayBroker>>());

    private static MessageEnvelope MakeEnvelope(string webhookId, object? body = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        WebhookId = webhookId,
        ReceivedAt = DateTimeOffset.UtcNow,
        Method = "POST",
        Path = "/test",
        RemoteAddress = "127.0.0.1",
        Body = body,
    };

    /// <summary>
    /// A request the relay server received.
    /// </summary>
    private sealed record RelayedRequest(string Method, string Path, string Query, string? ContentType, string Body);

    /// <summary>
    /// Kestrel server that records the last request and answers with a fixed status code.
    /// </summary>
    private sealed class RelayServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private RelayServer(WebApplication app) => _app = app;

        public RelayedRequest? LastRequest { get; private set; }

        public static async Task<RelayServer> StartAsync(int statusCode)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var app = builder.Build();
            var server = new RelayServer(app);

            app.Run(async context =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync(context.RequestAborted);

                server.LastRequest = new RelayedRequest(
                    context.Request.Method,
                    context.Request.Path.Value ?? string.Empty,
                    context.Request.QueryString.Value ?? string.Empty,
                    context.Request.ContentType,
                    body);

                context.Response.StatusCode = statusCode;
            });

            await app.StartAsync();
            return server;
        }

        public string Url(string pathAndQuery) => _app.Urls.First().TrimEnd('/') + pathAndQuery;

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
