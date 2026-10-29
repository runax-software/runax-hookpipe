using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Runax.Hookpipe.Api.Tests;

[Collection("API")]
public sealed class RoutingTests
{
    private const string Published = "hookpipe_messages_published_total";

    private const string Brokers = """
        brokers:
          push:
            type: stdout
          pr:
            type: stdout
          other:
            type: stdout
        """;

    [Fact]
    public async Task Route_HeaderCondition_PublishesToTheMatchingRule()
    {
        var id = NewId("header");
        using var host = new TestApiHost(Configuration(id, """
            route:
              - when: { header: X-Event-Type, eq: push }
                to: push
              - when: { header: X-Event-Type, eq: pull_request }
                to: pr
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, """{"test":"routing"}""", ("X-Event-Type", "push")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var metrics = await client.GetStringAsync("/metrics");
        TestApiHost.AssertMetric(metrics, Published, 1, ("webhook_id", id), ("broker_id", "push"));
        TestApiHost.AssertNoMetric(metrics, Published, ("webhook_id", id), ("broker_id", "other"));
    }

    [Fact]
    public async Task Route_NoConditionMatches_UsesTheFinalRule()
    {
        var id = NewId("final");
        using var host = new TestApiHost(Configuration(id, """
            route:
              - when: { header: X-Event-Type, eq: push }
                to: push
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, """{"test":"no-match"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), Published, 1,
            ("webhook_id", id), ("broker_id", "other"));
    }

    [Theory]
    [InlineData("{ jsonpath: $.action, eq: opened }", """{"action":"opened"}""")]
    [InlineData("{ jsonpath: $.event.type, eq: order.created }", """{"event":{"type":"order.created"}}""")]
    [InlineData("{ jsonpath: $.amount, gte: 100000 }", """{"amount":250000}""")]
    [InlineData("{ jsonpath: $.type, in: [invoice.paid, checkout.session.completed] }", """{"type":"invoice.paid"}""")]
    [InlineData("{ jsonpath: \"$.items[*].sku\", eq: A1 }", """{"items":[{"sku":"B2"},{"sku":"A1"}]}""")]
    [InlineData("{ header: X-Event-Type, contains: request }", """{}""")]
    [InlineData("{ header: X-Webhook-Source, exists: true }", """{}""")]
    [InlineData("{ not: { jsonpath: $.status, in: [paid, refunded] } }", """{"status":"pending"}""")]
    public async Task Route_Condition_MatchesTheRequest(string when, string body)
    {
        var id = NewId("condition");
        using var host = new TestApiHost(Configuration(id, $$"""
            route:
              - when: {{when}}
                to: push
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(
            Post(id, body, ("X-Event-Type", "pull_request"), ("X-Webhook-Source", "github")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), Published, 1,
            ("webhook_id", id), ("broker_id", "push"));
    }

    [Fact]
    public async Task Route_JsonPathOnNonJsonBody_DoesNotMatch()
    {
        var id = NewId("not-json");
        using var host = new TestApiHost(Configuration(id, """
            route:
              - when: { jsonpath: $.action, exists: true }
                to: push
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, "plain text"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), Published, 1,
            ("webhook_id", id), ("broker_id", "other"));
    }

    [Fact]
    public async Task Route_PathParameterCondition_RoutesOnTheUrl()
    {
        var id = NewId("path");
        using var host = new TestApiHost(Configuration(id, """
            path: ingest/{source}
            route:
              - when: { path: source, eq: github }
                to: push
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync("/webhooks/ingest/github", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), Published, 1,
            ("webhook_id", id), ("broker_id", "push"));
    }

    [Fact]
    public async Task Route_ToNone_AcceptsWithoutPublishing()
    {
        var id = NewId("drop");
        using var host = new TestApiHost(Configuration(id, """
            route:
              - when: { header: X-Event-Type, eq: ping }
                to: none
              - to: other
            """));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, "{}", ("X-Event-Type", "ping")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        TestApiHost.AssertNoMetric(await client.GetStringAsync("/metrics"), Published, ("webhook_id", id));
    }

    [Fact]
    public async Task To_SeveralTargets_PublishesToEach()
    {
        var id = NewId("fan-out");
        using var host = new TestApiHost(Configuration(id, "to: [push, pr]"));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var metrics = await client.GetStringAsync("/metrics");
        TestApiHost.AssertMetric(metrics, Published, 1, ("webhook_id", id), ("broker_id", "push"));
        TestApiHost.AssertMetric(metrics, Published, 1, ("webhook_id", id), ("broker_id", "pr"));
    }

    [Fact]
    public async Task Webhook_Accepted_ReturnsWebhookAndMessageIds()
    {
        var id = NewId("response");
        using var host = new TestApiHost(Configuration(id, "to: other"));
        var client = host.Factory.CreateClient();

        var response = await client.SendAsync(Post(id, "{}"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        body.GetProperty("status").GetString().Should().Be("accepted");
        body.GetProperty("webhook_id").GetString().Should().Be(id);
        Guid.TryParse(body.GetProperty("message_id").GetString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_DefaultPrefix_ServesOnlyUnderWebhooks()
    {
        var id = NewId("prefix");
        using var host = new TestApiHost(Configuration(id, "to: other"));
        var client = host.Factory.CreateClient();

        var underPrefix = await client.PostAsync($"/webhooks/{id}", new StringContent("{}"));
        var withoutPrefix = await client.PostAsync($"/{id}", new StringContent("{}"));

        underPrefix.StatusCode.Should().Be(HttpStatusCode.Accepted);
        withoutPrefix.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_RootPrefix_KeepsV1Urls()
    {
        var id = NewId("root");
        using var host = new TestApiHost($$"""
            version: 2
            server:
              paths:
                webhooks: /
            {{Brokers}}
            webhooks:
              {{id}}:
                path: /github/push
                to: other
            """);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync("/github/push", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Webhook_WrongMethod_Returns405()
    {
        var id = NewId("method");
        using var host = new TestApiHost(Configuration(id, "to: other"));
        var client = host.Factory.CreateClient();

        var response = await client.GetAsync($"/webhooks/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    private static string NewId(string name) => $"routing-{name}-{Guid.NewGuid():N}";

    private static string Configuration(string id, string webhook) => $$"""
        version: 2
        {{Brokers}}
        webhooks:
          {{id}}:
        {{Indent(webhook, 4)}}
        """;

    private static string Indent(string text, int spaces) =>
        string.Join('\n', text.Split('\n').Select(line => new string(' ', spaces) + line));

    private static HttpRequestMessage Post(string id, string body, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/{id}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        foreach (var (name, value) in headers)
            request.Headers.Add(name, value);

        return request;
    }
}
