using FluentAssertions;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="EventBridgeSink"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
[Collection("AWS")]
public sealed class EventBridgeSinkIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string BusEnv = "TEST_EVENTBRIDGE_BUS";
    private const string ServiceUrlEnv = "TEST_AWS_SERVICE_URL";
    private const string RegionEnv = "TEST_AWS_REGION";

    private EventBridgeSink _sink = null!;
    private IAmazonEventBridge _client = null!;
    private string _busName = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(ServiceUrlEnv, ServiceUrl);
        Environment.SetEnvironmentVariable(RegionEnv, Region);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");

        var config = new AmazonEventBridgeConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        _client = new AmazonEventBridgeClient("test", "test", config);

        // Create event bus
        _busName = $"hookpipe-test-{Guid.NewGuid():N}";
        await _client.CreateEventBusAsync(new CreateEventBusRequest { Name = _busName });
        Environment.SetEnvironmentVariable(BusEnv, _busName);

        // Create sink
        var sinkConfig = new SinkConfig
        {
            Id = "test-eventbridge",
            Type = "eventbridge",
            Settings = new Dictionary<string, string>
            {
                ["event_bus_env"] = BusEnv,
                ["region_env"] = RegionEnv,
                ["service_url_env"] = ServiceUrlEnv,
                ["source"] = "hookpipe-test",
                ["detail_type"] = "webhook-test",
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = EventBridgeSink.Create(sinkConfig, loggerFactory.CreateLogger<EventBridgeSink>());
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DeleteEventBusAsync(new DeleteEventBusRequest { Name = _busName });
    }

    public void Dispose()
    {
        _sink.Dispose();
        _client.Dispose();
        Environment.SetEnvironmentVariable(BusEnv, null);
        Environment.SetEnvironmentVariable(ServiceUrlEnv, null);
        Environment.SetEnvironmentVariable(RegionEnv, null);
    }

    [Fact]
    public async Task ProduceAsync_PutsEventToEventBus()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "eventbridge-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "eventbridge-integration" },
        };

        // PutEvents should succeed without throwing
        var act = () => _sink.ProduceAsync(envelope);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProduceAsync_ReturnsWithoutFailedEntries()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "no-failure-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        // Should not throw — FailedEntryCount should be 0
        var act = () => _sink.ProduceAsync(envelope);

        await act.Should().NotThrowAsync();
    }
}
