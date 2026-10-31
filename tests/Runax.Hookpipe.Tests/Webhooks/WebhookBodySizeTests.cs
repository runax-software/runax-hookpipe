using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Runax.Hookpipe.Hosting;
using Runax.Hookpipe.Webhooks;
using Shouldly;

namespace Runax.Hookpipe.Tests.Webhooks;

/// <summary>
/// Covers <c>webhooks.&lt;id&gt;.max_body_size</c>, which is applied per request, and
/// <c>server.max_body_size</c>, which library mode ignores with a warning.
/// </summary>
public sealed class WebhookBodySizeTests : IDisposable
{
    private readonly string _configurationPath =
        Path.Combine(Path.GetTempPath(), $"hookpipe-body-{Guid.NewGuid():N}.yaml");

    private readonly CollectingLoggerProvider _logs = new();

    [Fact]
    public async Task MaxBodySize_BodyOverTheLimit_Returns413()
    {
        await using var app = await StartAsync(Configuration("max_body_size: 64B"));

        var response = await Post(app, Body(200));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("payload_too_large");
        body.ShouldContain("github");
    }

    [Fact]
    public async Task MaxBodySize_BodyWithinTheLimit_Returns202()
    {
        await using var app = await StartAsync(Configuration("max_body_size: 1KB"));

        var response = await Post(app, Body(200));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task MaxBodySize_NotConfigured_AcceptsAnyBody()
    {
        await using var app = await StartAsync(Configuration());

        var response = await Post(app, Body(200));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task MaxBodySize_RaisedByAReload_AcceptsTheLargerBody()
    {
        await using var app = await StartAsync(Configuration("max_body_size: 64B"));
        (await Post(app, Body(200))).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        await File.WriteAllTextAsync(_configurationPath, Configuration("max_body_size: 1KB"));
        Reload(app).ShouldBeTrue();

        (await Post(app, Body(200))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task MaxBodySize_LoweredByAReload_RejectsTheSameBody()
    {
        await using var app = await StartAsync(Configuration("max_body_size: 1KB"));
        (await Post(app, Body(200))).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await File.WriteAllTextAsync(_configurationPath, Configuration("max_body_size: 64B"));
        Reload(app).ShouldBeTrue();

        (await Post(app, Body(200))).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ServerMaxBodySize_InLibraryMode_IsIgnoredWithAWarning()
    {
        await using var app = await StartAsync($"""
            version: 2
            server:
              max_body_size: 64B
            brokers:
              console:
                type: stdout
            webhooks:
              github:
                to: console
            """);

        var response = await Post(app, Body(200));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _logs.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Ignoring 'server.max_body_size'"),
            1);
    }

    [Fact]
    public async Task ServerMaxBodySize_TakenByTheHost_IsNotWarnedAbout()
    {
        long? taken = null;
        await using var app = await StartAsync($"""
            version: 2
            server:
              max_body_size: 64B
            brokers:
              console:
                type: stdout
            webhooks:
              github:
                to: console
            """, hookpipe => taken = hookpipe.TakeServerMaxBodySize());

        _ = await Post(app, Body(200));

        taken.ShouldBe(64);
        _logs.Entries.ShouldNotContain(entry => entry.Message.Contains("Ignoring 'server.max_body_size'"));
    }

    [Fact]
    public async Task MaxBodySize_ServerSupportsAPerRequestLimit_SetsIt()
    {
        var feature = new TestMaxRequestBodySizeFeature { MaxRequestBodySize = 10_000 };
        var context = RequestContext(feature, """{"ok":true}""");

        await HandleAsync(Configuration("max_body_size: 64B"), context);

        feature.MaxRequestBodySize.ShouldBe(64);
    }

    [Fact]
    public async Task MaxBodySize_ServerEnforcesItWhileReading_Returns413()
    {
        // No Content-Length, so the limit can only be enforced as the body is read.
        var context = RequestContext(new TestMaxRequestBodySizeFeature(), body: null);
        context.Request.Body = new ThrowingBodyStream();

        await HandleAsync(Configuration("max_body_size: 64B"), context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        ResponseText(context).ShouldContain("payload_too_large");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_configurationPath)) File.Delete(_configurationPath);
    }

    private static string Configuration(string? webhookLine = null) => $"""
        version: 2
        brokers:
          console:
            type: stdout
        webhooks:
          github:
            {webhookLine ?? "methods: [POST]"}
            to: console
        """;

    private static string Body(int bytes) => $$"""{"padding":"{{new string('x', bytes)}}"}""";

    private static Task<HttpResponseMessage> Post(WebApplication app, string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.ContentLength = Encoding.UTF8.GetByteCount(body);

        return app.GetTestClient().PostAsync("/webhooks/github", content);
    }

    private static bool Reload(WebApplication app) =>
        app.Services.GetRequiredService<HookpipeRuntime>().ConfigurationProvider.Reload();

    private static DefaultHttpContext RequestContext(IHttpMaxRequestBodySizeFeature feature, string? body)
    {
        var context = new DefaultHttpContext();
        context.Features.Set(feature);
        context.Request.Method = "POST";
        context.Request.Path = "/webhooks/github";
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        if (body is null) return context;

        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        return context;
    }

    private static string ResponseText(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    /// <summary>
    /// Runs the handler straight on an <see cref="HttpContext"/>, to reach what the server's
    /// per-request body-size feature drives. Publishing fails, because no broker is connected.
    /// </summary>
    private async Task HandleAsync(string yaml, HttpContext context)
    {
        await File.WriteAllTextAsync(_configurationPath, yaml);

        var runtime = new HookpipeRuntime(new HookpipeOptions(_configurationPath, false), NullLoggerFactory.Instance);
        await new WebhookHandler("github", NullLogger.Instance, runtime).HandleAsync(context);
    }

    private async Task<WebApplication> StartAsync(string yaml, Action<HookpipeConfigurator>? configure = null)
    {
        await File.WriteAllTextAsync(_configurationPath, yaml);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddRunaxHookpipe(hookpipe =>
        {
            hookpipe.LoadYaml(_configurationPath, reloadOnChange: false);
            configure?.Invoke(hookpipe);
        });

        var app = builder.Build();
        app.MapHookpipe();
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Stands in for the server's per-request body-size limit.
    /// </summary>
    private sealed class TestMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; }
    }

    /// <summary>
    /// A body the server rejects as it's read, the way Kestrel does when the length wasn't declared.
    /// </summary>
    private sealed class ThrowingBodyStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Keeps every log entry so a test can assert on startup warnings.
    /// </summary>
    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(Entries);

        public void Dispose() { }

        private sealed class CollectingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
