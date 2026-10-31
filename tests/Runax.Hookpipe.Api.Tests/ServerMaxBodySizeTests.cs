using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Runax.Hookpipe.Api.Tests;

/// <summary>
/// Covers the service applying <c>server.max_body_size</c> to Kestrel, and answering 413 for a
/// webhook over its own <c>max_body_size</c>.
/// </summary>
[Collection("API")]
public sealed class ServerMaxBodySizeTests
{
    private const string Brokers = """
        version: 2
        brokers:
          console:
            type: stdout
        """;

    [Fact]
    public void ServerMaxBodySize_Configured_SetsKestrelsLimit()
    {
        using var host = new ServiceHost($"""
            {Brokers}
            server:
              max_body_size: 64KB
            webhooks:
              github:
                to: console
            """);

        host.KestrelMaxRequestBodySize.ShouldBe(64 * 1024);
    }

    [Fact]
    public void ServerMaxBodySize_Absent_LeavesKestrelsDefault()
    {
        using var host = new ServiceHost($"""
            {Brokers}
            webhooks:
              github:
                to: console
            """);

        host.KestrelMaxRequestBodySize.ShouldBe(new KestrelServerOptions().Limits.MaxRequestBodySize);
    }

    [Fact]
    public async Task WebhookMaxBodySize_BodyOverTheLimit_Returns413()
    {
        using var host = new ServiceHost($"""
            {Brokers}
            webhooks:
              github:
                max_body_size: 64B
                to: console
            """);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync("/webhooks/github",
            new StringContent($$"""{"padding":"{{new string('x', 200)}}"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync()).ShouldContain("payload_too_large");
    }

    [Fact]
    public async Task WebhookMaxBodySize_BodyWithinTheLimit_Returns202()
    {
        using var host = new ServiceHost($"""
            {Brokers}
            webhooks:
              github:
                max_body_size: 1KB
                to: console
            """);
        var client = host.Factory.CreateClient();

        var response = await client.PostAsync("/webhooks/github",
            new StringContent($$"""{"padding":"{{new string('x', 200)}}"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Runs the service on a temporary configuration file and restores <c>HOOKPIPE_CONFIG_PATH</c>
    /// when disposed.
    /// </summary>
    private sealed class ServiceHost : IDisposable
    {
        private readonly string _configurationPath;
        private readonly string? _previousConfigurationPath;

        public ServiceHost(string yaml)
        {
            _configurationPath = Path.Combine(Path.GetTempPath(), $"hookpipe-kestrel-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(_configurationPath, yaml);

            _previousConfigurationPath = Environment.GetEnvironmentVariable("HOOKPIPE_CONFIG_PATH");
            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _configurationPath);

            Factory = new WebApplicationFactory<Program>();
        }

        public WebApplicationFactory<Program> Factory { get; }

        /// <summary>
        /// The limit the service configured Kestrel with, however the test host serves requests.
        /// </summary>
        public long? KestrelMaxRequestBodySize =>
            Factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize;

        public void Dispose()
        {
            Factory.Dispose();
            Environment.SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _previousConfigurationPath);

            if (File.Exists(_configurationPath)) File.Delete(_configurationPath);
        }
    }
}
