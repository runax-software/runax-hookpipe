using System.Text;
using System.Text.Json;
using FluentAssertions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Webhooks;

public sealed class EnvelopeBuilderTests
{
    private readonly EnvelopeBuilder _builder = new(Substitute.For<ILogger<EnvelopeBuilder>>());

    private const string WebhookId = "test-webhook";

    private static WebhookConfiguration MakeWebhook(
        bool includeBody = true,
        bool includeHeaders = false,
        List<string>? headerNames = null,
        Dictionary<string, string>? metadata = null) => new()
        {
            Path = "test",
            To = [new Target("console", null)],
            Message = new MessageConfiguration
            {
                Body = includeBody,
                IncludeHeaders = includeHeaders,
                HeaderNames = headerNames,
                Metadata = metadata ?? []
            }
        };

    private static HttpContext MakeContext(string method = "POST", string path = "/test", string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (body is not null)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.Body = stream;
            context.Request.ContentLength = stream.Length;
            context.Request.ContentType = "application/json";
        }

        return context;
    }

    [Fact]
    public async Task BuildAsync_SetsBasicFields()
    {
        var context = MakeContext("POST", "/test");
        var webhook = MakeWebhook(includeBody: false);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Id.Should().NotBeNullOrEmpty();
        envelope.WebhookId.Should().Be(WebhookId);
        envelope.Method.Should().Be("POST");
        envelope.Path.Should().Be("/test");
        envelope.ReceivedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BuildAsync_IncludeBody_ParsesJson()
    {
        var context = MakeContext(body: """{"key":"value"}""");
        var webhook = MakeWebhook(includeBody: true);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Body.Should().NotBeNull();
        var json = (JsonElement)envelope.Body!;
        json.GetProperty("key").GetString().Should().Be("value");
    }

    [Fact]
    public async Task BuildAsync_IncludeBody_NonJsonFallsBackToString()
    {
        var context = MakeContext(body: "plain text body");
        var webhook = MakeWebhook(includeBody: true);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Body.Should().Be("plain text body");
    }

    [Fact]
    public async Task BuildAsync_ExcludeBody_BodyIsNull()
    {
        var context = MakeContext(body: """{"key":"value"}""");
        var webhook = MakeWebhook(includeBody: false);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Body.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_IncludeAllHeaders()
    {
        var context = MakeContext();
        context.Request.Headers["X-Custom"] = "custom-value";
        context.Request.Headers["X-Other"] = "other-value";
        var webhook = MakeWebhook(includeBody: false, includeHeaders: true);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Headers.Should().ContainKey("X-Custom");
        envelope.Headers.Should().ContainKey("X-Other");
    }

    [Fact]
    public async Task BuildAsync_HeaderNames_OnlyIncludesListed()
    {
        var context = MakeContext();
        context.Request.Headers["X-Wanted"] = "yes";
        context.Request.Headers["X-Unwanted"] = "no";
        var webhook = MakeWebhook(includeBody: false, includeHeaders: true, headerNames: ["X-Wanted"]);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Headers.Should().ContainKey("X-Wanted");
        envelope.Headers.Should().NotContainKey("X-Unwanted");
    }

    [Fact]
    public async Task BuildAsync_HeadersDisabled_NoHeaders()
    {
        var context = MakeContext();
        context.Request.Headers["X-Custom"] = "value";
        var webhook = MakeWebhook(includeBody: false, includeHeaders: false);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_StaticMetadata_Included()
    {
        var context = MakeContext();
        var webhook = MakeWebhook(
            includeBody: false,
            metadata: new Dictionary<string, string> { ["env"] = "production" });

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("production");
    }

    [Fact]
    public async Task BuildAsync_PathParamMetadata_Resolved()
    {
        var context = MakeContext(path: "/ingest/github");
        var webhook = MakeWebhook(
            includeBody: false,
            metadata: new Dictionary<string, string> { ["source"] = "{source}" });
        var pathParams = new Dictionary<string, string> { ["source"] = "github" };

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook, pathParams);

        envelope.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("github");
    }

    [Fact]
    public async Task BuildAsync_NoMetadataConfiguration_MetadataEmpty()
    {
        var context = MakeContext();
        var webhook = MakeWebhook(includeBody: false);

        var envelope = await _builder.BuildAsync(context, WebhookId, webhook);

        envelope.Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_GeneratesUniqueIds()
    {
        var context1 = MakeContext();
        var context2 = MakeContext();
        var webhook = MakeWebhook(includeBody: false);

        var envelope1 = await _builder.BuildAsync(context1, WebhookId, webhook);
        var envelope2 = await _builder.BuildAsync(context2, WebhookId, webhook);

        envelope1.Id.Should().NotBe(envelope2.Id);
    }
}
