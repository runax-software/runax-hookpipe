using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hookpipe.Core.Tests.API;

public sealed class ApiMetricsTests
{
    [Fact]
    public async Task WebhookRequest_SuccessfulSink_IncrementsMessagesProducedMetric()
    {
        var endpointId = $"metrics-success-{Guid.NewGuid():N}";
        const string sinkId = "stdout-dev";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: {{sinkId}}
                message:
                  include_body: true
            sinks:
              - id: {{sinkId}}
                type: stdout
            """;

        using var host = new TestApiHost(yaml);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"hello":"metrics"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var metrics = await client.GetStringAsync("/metrics");
        AssertContainsMetric(
            metrics,
            "hookpipe_messages_produced_total",
            ("endpoint_id", endpointId),
            ("sink_id", sinkId));
    }

    [Fact]
    public async Task WebhookRequest_FailedSink_IncrementsSinkErrorsMetric()
    {
        var endpointId = $"metrics-failure-{Guid.NewGuid():N}";
        var urlEnv = $"HOOKPIPE_TEST_HTTP_RELAY_URL_{Guid.NewGuid():N}";
        const string sinkId = "http-fail";
        var yaml = $$"""
            endpoints:
              - id: {{endpointId}}
                path: /{{endpointId}}
                methods:
                  - POST
                sink: {{sinkId}}
                message:
                  include_body: true
            sinks:
              - id: {{sinkId}}
                type: http
                settings:
                  url_env: {{urlEnv}}
                  timeout_seconds: "1"
            """;

        using var host = new TestApiHost(yaml, new Dictionary<string, string?>
        {
            [urlEnv] = "http://127.0.0.1:1/fail",
        });
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync(
            $"/{endpointId}",
            new StringContent("""{"hello":"metrics"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var metrics = await client.GetStringAsync("/metrics");
        AssertContainsMetric(
            metrics,
            "hookpipe_sink_errors_total",
            ("endpoint_id", endpointId),
            ("sink_id", sinkId));
    }

    private static void AssertContainsMetric(
        string metrics,
        string metricName,
        params (string Name, string Value)[] labels)
    {
        var lines = metrics.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().Contain(line =>
            line.StartsWith($"{metricName}{{", StringComparison.Ordinal) &&
            labels.All(label =>
                line.Contains($"{label.Name}=\"{label.Value}\"", StringComparison.Ordinal)) &&
            line.EndsWith(" 1", StringComparison.Ordinal));
    }

    private sealed class TestApiHost : IDisposable
    {
        private readonly string _configPath;
        private readonly string? _previousConfigPath;
        private readonly Dictionary<string, string?> _previousEnv = [];

        public TestApiHost(string yaml, Dictionary<string, string?>? env = null)
        {
            _configPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(_configPath, yaml);

            _previousConfigPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH");
            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _configPath);

            if (env is not null)
                foreach (var (key, value) in env)
                {
                    _previousEnv[key] = Environment.GetEnvironmentVariable(key);
                    Environment.SetEnvironmentVariable(key, value);
                }

            Factory = new WebApplicationFactory<Program>();
        }

        public WebApplicationFactory<Program> Factory { get; }

        public void Dispose()
        {
            Factory.Dispose();

            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _previousConfigPath);
            foreach (var (key, value) in _previousEnv)
                Environment.SetEnvironmentVariable(key, value);

            if (File.Exists(_configPath))
                File.Delete(_configPath);
        }
    }
}
