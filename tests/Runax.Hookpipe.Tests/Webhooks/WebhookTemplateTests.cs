using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Runax.Hookpipe.Brokers;
using Runax.Hookpipe.Hosting;
using Runax.Hookpipe.Models;

namespace Runax.Hookpipe.Tests.Webhooks;

/// <summary>
/// Templates in topics, metadata, and condition values, through a real host. The <c>relay</c> broker
/// is replaced by a fake after startup, so tests see the rendered topic and envelope.
/// </summary>
public sealed class WebhookTemplateTests : IDisposable
{
    private readonly string _configurationPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");
    private readonly IBroker _relay = Substitute.For<IBroker>();

    [Fact]
    public async Task TemplatedTopic_IsRenderedFromThePathAndBody()
    {
        await using var app = await StartAsync("""
            ingest:
              path: ingest/{source}
              to: "relay:{source}.{$.type}"
            """);

        var response = await PostAsync(app, "/webhooks/ingest/github", """{"type":"invoice.paid"}""");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _relay.Received(1).PublishAsync("github.invoice.paid", Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TemplatedTopic_UsesTheDefaultWhenTheValueIsMissing()
    {
        await using var app = await StartAsync("""
            events:
              to: "relay:events.{$.type:-other}"
            """);

        var response = await PostAsync(app, "/webhooks/events", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _relay.Received(1).PublishAsync("events.other", Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnresolvedTopic_SkipsOnlyThatTarget()
    {
        await using var app = await StartAsync("""
            events:
              to: ["relay:{$.type}", console]
            """);

        var response = await PostAsync(app, "/webhooks/events", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _relay.DidNotReceiveWithAnyArgs().PublishAsync(default, default!, default);
    }

    [Fact]
    public async Task UnresolvedTopic_OnTheOnlyTarget_FailsThePublish()
    {
        await using var app = await StartAsync("""
            events:
              to: "relay:{$.type}"
            """);

        var response = await PostAsync(app, "/webhooks/events", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("publish_failed");
        await _relay.DidNotReceiveWithAnyArgs().PublishAsync(default, default!, default);
    }

    [Fact]
    public async Task RenderedTopicOutsideTheAllowedCharacters_IsNotPublished()
    {
        await using var app = await StartAsync("""
            events:
              to: "relay:events.{$.type}"
            """);

        var response = await PostAsync(app, "/webhooks/events", """{"type":"a/b"}""");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await _relay.DidNotReceiveWithAnyArgs().PublishAsync(default, default!, default);
    }

    [Fact]
    public async Task MetadataTemplates_AreRenderedIntoTheEnvelopeAndTopic()
    {
        await using var app = await StartAsync("""
            ingest:
              path: ingest/{source}
              message:
                metadata:
                  tenant: "{header.X-Tenant}"
                  source: "{source}"
              to: "relay:{metadata.tenant}.events"
            """);

        var response = await PostAsync(app, "/webhooks/ingest/github", "{}", ("X-Tenant", "acme"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _relay.Received(1).PublishAsync("acme.events",
            Arg.Is<MessageEnvelope>(envelope =>
                envelope.Metadata["tenant"] == "acme" && envelope.Metadata["source"] == "github"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnresolvedMetadata_FailsThePublish()
    {
        await using var app = await StartAsync("""
            events:
              message:
                metadata:
                  tenant: "{header.X-Tenant}"
              to: relay:events
            """);

        var response = await PostAsync(app, "/webhooks/events", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await _relay.DidNotReceiveWithAnyArgs().PublishAsync(default, default!, default);
    }

    [Fact]
    public async Task ConditionValuePlaceholder_ComparesWithThePathParameter()
    {
        await using var app = await StartAsync("""
            tenant:
              path: tenants/{tenant}
              route:
                - when: { header: X-Tenant, eq: "{tenant}" }
                  to: relay:matched
                - to: relay:other
            """);

        await PostAsync(app, "/webhooks/tenants/acme", "{}", ("X-Tenant", "acme"));
        await PostAsync(app, "/webhooks/tenants/acme", "{}", ("X-Tenant", "globex"));

        await _relay.Received(1).PublishAsync("matched", Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        await _relay.Received(1).PublishAsync("other", Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_configurationPath))
            File.Delete(_configurationPath);
    }

    private async Task<WebApplication> StartAsync(string webhooks)
    {
        var indented = string.Join('\n', webhooks.Split('\n').Select(line => "  " + line));
        File.WriteAllText(_configurationPath, $"""
            version: 2
            brokers:
              relay:
                type: http
                settings:
                  url: http://127.0.0.1:1
              console:
                type: stdout
            webhooks:
            {indented}
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath, reloadOnChange: false));

        var app = builder.Build();
        app.MapHookpipe();
        await app.StartAsync();

        app.Services.GetRequiredService<HookpipeRuntime>().Brokers["relay"] = _relay;
        return app;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        WebApplication app,
        string path,
        string body,
        params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
            request.Headers.Add(name, value);

        return await app.GetTestClient().SendAsync(request);
    }
}
