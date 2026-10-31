using System.Net;
using System.Text;
using Shouldly;

namespace Runax.Hookpipe.Api.Tests;

[Collection("API")]
public sealed class ApiMetricsTests
{
    [Fact]
    public async Task WebhookRequest_SuccessfulPublish_IncrementsMessagesPublishedMetric()
    {
        var webhookId = $"metrics-success-{Guid.NewGuid():N}";
        var yaml = $$"""
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              {{webhookId}}:
                to: console
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/webhooks/{webhookId}",
            new StringContent("""{"hello":"metrics"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), "hookpipe_messages_published_total", 1,
            ("webhook_id", webhookId), ("broker_id", "console"));
    }

    [Fact]
    public async Task WebhookRequest_FailedPublish_IncrementsPublishErrorsMetric()
    {
        var webhookId = $"metrics-failure-{Guid.NewGuid():N}";
        var yaml = $$"""
            version: 2
            brokers:
              relay:
                type: http
                settings:
                  url: http://127.0.0.1:1/fail
                  timeout: 1s
            webhooks:
              {{webhookId}}:
                to: relay
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/webhooks/{webhookId}",
            new StringContent("""{"hello":"metrics"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldContain("publish_failed");
        TestApiHost.AssertMetric(await client.GetStringAsync("/metrics"), "hookpipe_publish_errors_total", 1,
            ("webhook_id", webhookId), ("broker_id", "relay"));
    }

    [Fact]
    public async Task Metrics_ExportEveryHookpipeMetricWithItsNameAndType()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tokenEnv = $"HOOKPIPE_TEST_TOKEN_{suffix}";
        var yaml = $$"""
            version: 2
            brokers:
              console:
                type: stdout
              relay:
                type: http
                settings:
                  url: http://127.0.0.1:1/fail
                  timeout: 1s
            webhooks:
              secured-{{suffix}}:
                validate:
                  - type: bearer
                    token: ${{{tokenEnv}}}
                to: console
              broken-{{suffix}}:
                to: relay
              templated-{{suffix}}:
                to: "relay:{$.type}"
            """;

        using var host = new TestApiHost(yaml, new Dictionary<string, string?> { [tokenEnv] = "secret-token" });
        var client = host.Factory.CreateClient();

        var unauthorized = await client.PostAsync($"/webhooks/secured-{suffix}", new StringContent("{}"));
        var authorized = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/secured-{suffix}")
        {
            Content = new StringContent("{}"),
        };
        authorized.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret-token");
        var accepted = await client.SendAsync(authorized);
        var failed = await client.PostAsync($"/webhooks/broken-{suffix}", new StringContent("{}"));
        var unresolved = await client.PostAsync($"/webhooks/templated-{suffix}", new StringContent("{}"));

        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        unresolved.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.ShouldContain("# TYPE hookpipe_requests_total counter");
        metrics.ShouldContain("# TYPE hookpipe_messages_published_total counter");
        metrics.ShouldContain("# TYPE hookpipe_publish_errors_total counter");
        metrics.ShouldContain("# TYPE hookpipe_validation_failures_total counter");
        metrics.ShouldContain("# TYPE hookpipe_template_failures_total counter");
        metrics.ShouldContain("# TYPE hookpipe_request_duration_seconds histogram");
        metrics.ShouldNotContain("runax_hookpipe_");
        TestApiHost.AssertMetric(metrics, "hookpipe_validation_failures_total", 1,
            ("webhook_id", $"secured-{suffix}"), ("validator_type", "bearer"));
        TestApiHost.AssertMetric(metrics, "hookpipe_requests_total", 1,
            ("webhook_id", $"secured-{suffix}"), ("status_code", "401"));
        TestApiHost.AssertMetric(metrics, "hookpipe_template_failures_total", 1,
            ("broker_id", "relay"), ("reason", "template_unresolved"));
    }
}
