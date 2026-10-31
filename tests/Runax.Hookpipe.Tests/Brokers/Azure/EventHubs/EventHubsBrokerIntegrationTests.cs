using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Azure.EventHubs;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Azure.EventHubs;

/// <summary>
/// Integration tests for <see cref="EventHubsBroker"/>.
/// Requires Azure Event Hubs emulator running on localhost:5674 (docker compose up eventhub-emulator).
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventHubsBrokerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Endpoint=sb://localhost:5674/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string EventHubName = "hookpipe-test";
    private const string ConsumerGroup = "hookpipe-cg";

    private EventHubsBroker _broker = null!;
    private EventHubConsumerClient _consumer = null!;

    public async ValueTask InitializeAsync()
    {
        _consumer = new EventHubConsumerClient(ConsumerGroup, ConnectionString, EventHubName);
        await WaitForEventHubAsync();

        var settings = new EventHubsBrokerSettings { ConnectionString = ConnectionString };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = EventHubsBroker.Create("test-eventhub", settings, loggerFactory.CreateLogger<EventHubsBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _consumer.CloseAsync();
    }

    [Fact]
    public async Task ProduceAsync_PublishesEventToHub()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "eventhub-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "eventhub-integration" },
        };

        await _broker.PublishAsync(EventHubName, envelope);

        var received = await ReceiveEventAsync(messageId);
        received.ShouldNotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(received!.EventBody);
        deserialized.GetProperty("webhookId").GetString().ShouldBe("eventhub-integration-test");
        deserialized.GetProperty("method").GetString().ShouldBe("POST");
    }

    [Fact]
    public async Task ProduceAsync_EventHasApplicationProperties()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "eventhub-attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(EventHubName, envelope);

        var received = await ReceiveEventAsync(messageId);
        received.ShouldNotBeNull();

        received!.MessageId.ShouldBe(messageId);
        received.ContentType.ShouldBe("application/json");
        received.Properties["hookpipe.message.id"].ShouldBe(messageId);
        received.Properties["hookpipe.webhook.id"].ShouldBe("eventhub-attr-test");
    }

    private async Task WaitForEventHubAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var partitionIds = await _consumer.GetPartitionIdsAsync();
                if (partitionIds.Length > 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException("Azure Event Hubs emulator did not become ready.", lastException);
    }

    private async Task<EventData?> ReceiveEventAsync(string messageId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await foreach (var partitionEvent in _consumer.ReadEventsAsync(true, new ReadEventOptions(), cts.Token))
            {
                var eventData = partitionEvent.Data;
                if (eventData is not null && eventData.MessageId == messageId)
                {
                    return eventData;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return null;
    }
}
