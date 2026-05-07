using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Tests.Services;

public sealed class EnvelopeBuilderTests
{
    private static EndpointConfig MakeEndpoint(
        bool includeBody = true,
        bool includeHeaders = false,
        List<string>? headerFilter = null,
        Dictionary<string, string>? metadata = null) => new()
    {
        Id = "test-endpoint",
        Path = "/test",
        Sink = "stdout",
        Message = new MessageConfig
        {
            IncludeBody = includeBody,
            IncludeHeaders = includeHeaders,
            HeaderFilter = headerFilter,
            Metadata = metadata
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
        var endpoint = MakeEndpoint(includeBody: false);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Id.Should().NotBeNullOrEmpty();
        envelope.EndpointId.Should().Be("test-endpoint");
        envelope.Method.Should().Be("POST");
        envelope.Path.Should().Be("/test");
        envelope.ReceivedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BuildAsync_IncludeBody_ParsesJson()
    {
        var context = MakeContext(body: """{"key":"value"}""");
        var endpoint = MakeEndpoint(includeBody: true);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Body.Should().NotBeNull();
        var json = (JsonElement)envelope.Body!;
        json.GetProperty("key").GetString().Should().Be("value");
    }

    [Fact]
    public async Task BuildAsync_IncludeBody_NonJsonFallsBackToString()
    {
        var context = MakeContext(body: "plain text body");
        var endpoint = MakeEndpoint(includeBody: true);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Body.Should().Be("plain text body");
    }

    [Fact]
    public async Task BuildAsync_ExcludeBody_BodyIsNull()
    {
        var context = MakeContext(body: """{"key":"value"}""");
        var endpoint = MakeEndpoint(includeBody: false);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Body.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_IncludeAllHeaders()
    {
        var context = MakeContext();
        context.Request.Headers["X-Custom"] = "custom-value";
        context.Request.Headers["X-Other"] = "other-value";
        var endpoint = MakeEndpoint(includeBody: false, includeHeaders: true);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Headers.Should().ContainKey("X-Custom");
        envelope.Headers.Should().ContainKey("X-Other");
    }

    [Fact]
    public async Task BuildAsync_HeaderFilter_OnlyIncludesFiltered()
    {
        var context = MakeContext();
        context.Request.Headers["X-Wanted"] = "yes";
        context.Request.Headers["X-Unwanted"] = "no";
        var endpoint = MakeEndpoint(includeBody: false, includeHeaders: true, headerFilter: ["X-Wanted"]);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Headers.Should().ContainKey("X-Wanted");
        envelope.Headers.Should().NotContainKey("X-Unwanted");
    }

    [Fact]
    public async Task BuildAsync_HeadersDisabled_NoHeaders()
    {
        var context = MakeContext();
        context.Request.Headers["X-Custom"] = "value";
        var endpoint = MakeEndpoint(includeBody: false, includeHeaders: false);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_StaticMetadata_Included()
    {
        var context = MakeContext();
        var endpoint = MakeEndpoint(
            includeBody: false,
            metadata: new Dictionary<string, string> { ["env"] = "production" });

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Metadata.Should().ContainKey("env").WhoseValue.Should().Be("production");
    }

    [Fact]
    public async Task BuildAsync_PathParamMetadata_Resolved()
    {
        var context = MakeContext(path: "/ingest/github");
        var endpoint = MakeEndpoint(
            includeBody: false,
            metadata: new Dictionary<string, string> { ["source"] = "{source}" });
        var pathParams = new Dictionary<string, string> { ["source"] = "github" };

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint, pathParams);

        envelope.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("github");
    }

    [Fact]
    public async Task BuildAsync_NoMetadataConfig_MetadataEmpty()
    {
        var context = MakeContext();
        var endpoint = MakeEndpoint(includeBody: false);

        var envelope = await EnvelopeBuilder.BuildAsync(context, endpoint);

        envelope.Metadata.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_GeneratesUniqueIds()
    {
        var context1 = MakeContext();
        var context2 = MakeContext();
        var endpoint = MakeEndpoint(includeBody: false);

        var envelope1 = await EnvelopeBuilder.BuildAsync(context1, endpoint);
        var envelope2 = await EnvelopeBuilder.BuildAsync(context2, endpoint);

        envelope1.Id.Should().NotBe(envelope2.Id);
    }
}
