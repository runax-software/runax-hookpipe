using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hookpipe.Core.Tests.API;

[Collection("API")]
public sealed class RoutingTests
{
    [Fact]
    public async Task Routing_HeaderMatch_RoutesToCorrectSink()
    {
        var endpointId = $"routing-header-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-default
                routing:
                  - match:
                      header: X-Event-Type
                      operator: value
                      pattern: push
                    sinks:
                      - stdout-push
                  - match:
                      header: X-Event-Type
                      operator: value
                      pattern: pull_request
                    sinks:
                      - stdout-pr
                message:
                  include_body: true
            sinks:
              - id: stdout-default
                type: stdout
              - id: stdout-push
                type: stdout
              - id: stdout-pr
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/{endpointId}")
        {
            Content = new StringContent("""{"test":"routing"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Event-Type", "push");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify via metrics that stdout-push got the message
        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-push\"}} 1");
    }

    [Fact]
    public async Task Routing_NoMatch_FallsBackToDefaultSinks()
    {
        var endpointId = $"routing-default-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-fallback
                routing:
                  - match:
                      header: X-Event-Type
                      operator: value
                      pattern: push
                    sinks:
                      - stdout-push
                message:
                  include_body: true
            sinks:
              - id: stdout-fallback
                type: stdout
              - id: stdout-push
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"test":"no-match"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-fallback\"}} 1");
    }

    [Fact]
    public async Task Routing_DefaultRule_MatchesWhenNoOtherRuleDoes()
    {
        var endpointId = $"routing-catchall-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-never
                routing:
                  - match:
                      header: X-Event-Type
                      operator: value
                      pattern: push
                    sinks:
                      - stdout-push
                  - default: true
                    sinks:
                      - stdout-catchall
                message:
                  include_body: true
            sinks:
              - id: stdout-never
                type: stdout
              - id: stdout-push
                type: stdout
              - id: stdout-catchall
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"test":"catchall"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-catchall\"}} 1");
    }

    [Fact]
    public async Task Routing_BodyMatch_RoutesOnJsonField()
    {
        var endpointId = $"routing-body-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-default
                routing:
                  - match:
                      body: $.action
                      operator: value
                      pattern: completed
                    sinks:
                      - stdout-completed
                message:
                  include_body: true
            sinks:
              - id: stdout-default
                type: stdout
              - id: stdout-completed
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"action":"completed","id":123}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-completed\"}} 1");
    }

    [Fact]
    public async Task Routing_NestedBodyPath_RoutesOnNestedField()
    {
        var endpointId = $"routing-nested-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-default
                routing:
                  - match:
                      body: $.event.type
                      operator: value
                      pattern: order.created
                    sinks:
                      - stdout-orders
                message:
                  include_body: true
            sinks:
              - id: stdout-default
                type: stdout
              - id: stdout-orders
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"event":{"type":"order.created","id":"abc"}}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-orders\"}} 1");
    }

    [Fact]
    public async Task Routing_ContainsOperator_MatchesSubstring()
    {
        var endpointId = $"routing-contains-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-default
                routing:
                  - match:
                      header: Content-Type
                      operator: contains
                      pattern: json
                    sinks:
                      - stdout-json
                message:
                  include_body: true
            sinks:
              - id: stdout-default
                type: stdout
              - id: stdout-json
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"test":"contains"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-json\"}} 1");
    }

    [Fact]
    public async Task Routing_ExistsOperator_MatchesWhenHeaderPresent()
    {
        var endpointId = $"routing-exists-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-default
                routing:
                  - match:
                      header: X-Webhook-Source
                      operator: exists
                    sinks:
                      - stdout-sourced
                message:
                  include_body: true
            sinks:
              - id: stdout-default
                type: stdout
              - id: stdout-sourced
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/{endpointId}")
        {
            Content = new StringContent("""{"test":"exists"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Webhook-Source", "github");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-sourced\"}} 1");
    }

    [Fact]
    public async Task Routing_NoRoutingConfig_UsesDefaultSinks()
    {
        var endpointId = $"routing-none-{Guid.NewGuid():N}";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: stdout-dev
                message:
                  include_body: true
            sinks:
              - id: stdout-dev
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"test":"no-routing"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.Should().Contain($"hookpipe_messages_produced_total{{endpoint_id=\"{endpointId}\",sink_id=\"stdout-dev\"}} 1");
    }

    private sealed class TestApiHost : IDisposable
    {
        private readonly string _configPath;
        private readonly string? _previousConfigPath;

        public TestApiHost(string yaml)
        {
            _configPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(_configPath, yaml);

            _previousConfigPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH");
            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _configPath);

            Factory = new WebApplicationFactory<Program>();
        }

        public WebApplicationFactory<Program> Factory { get; }

        public void Dispose()
        {
            Factory.Dispose();
            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _previousConfigPath);

            if (File.Exists(_configPath))
                File.Delete(_configPath);
        }
    }
}
