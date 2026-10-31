using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.EventBridge;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Aws.EventBridge;

/// <summary>
/// Integration tests for <see cref="EventBridgeBroker"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
[Collection("AWS")]
public sealed class EventBridgeBrokerIntegrationTests : IAsyncLifetime
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string DetailType = "webhook-test";

    private EventBridgeBroker _broker = null!;
    private IAmazonEventBridge _client = null!;
    private string _busName = null!;

    public async ValueTask InitializeAsync()
    {
        var configuration = new AmazonEventBridgeConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        _client = new AmazonEventBridgeClient("test", "test", configuration);

        // Create event bus
        _busName = $"hookpipe-test-{Guid.NewGuid():N}";
        await _client.CreateEventBusAsync(new CreateEventBusRequest { Name = _busName });

        // Create broker
        var settings = new EventBridgeBrokerSettings
        {
            EventBusName = _busName,
            Source = "hookpipe-test",
            Region = Region,
            ServiceUrl = ServiceUrl,
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = EventBridgeBroker.Create("test-eventbridge", settings, loggerFactory.CreateLogger<EventBridgeBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DeleteEventBusAsync(new DeleteEventBusRequest { Name = _busName });
        _broker.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task ProduceAsync_PutsEventToEventBus()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "eventbridge-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "eventbridge-integration" },
        };

        // PutEvents should succeed without throwing
        var act = () => _broker.PublishAsync(DetailType, envelope);

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task ProduceAsync_ReturnsWithoutFailedEntries()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "no-failure-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        // Should not throw — FailedEntryCount should be 0
        var act = () => _broker.PublishAsync(DetailType, envelope);

        await Should.NotThrowAsync(act);
    }
}
