using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="EventHubSink"/>.
/// Requires Azure Event Hubs emulator running on localhost:5674 (docker compose up eventhub-emulator).
/// </summary>
[Trait("Category", "Integration")]
public sealed class EventHubSinkIntegrationTests : IAsyncLifetime
{
    private const string ConnectionStringEnv = "TEST_EVENTHUB_INTEGRATION_CONNECTION_STRING";
    private const string ConnectionString =
        "Endpoint=sb://localhost:5674/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string EventHubName = "hookpipe-test";
    private const string ConsumerGroup = "hookpipe-cg";

    private EventHubSink _sink = null!;
    private EventHubConsumerClient _consumer = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnv, ConnectionString);

        _consumer = new EventHubConsumerClient(ConsumerGroup, ConnectionString, EventHubName);
        await WaitForEventHubAsync();

        var sinkConfig = new SinkConfig
        {
            Id = "test-eventhub",
            Type = "eventhub",
            Settings = new Dictionary<string, string>
            {
                ["connection_string_env"] = ConnectionStringEnv,
                ["event_hub_name"] = EventHubName,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = EventHubSink.Create(sinkConfig, loggerFactory.CreateLogger<EventHubSink>());
    }

    public async Task DisposeAsync()
    {
        await _sink.DisposeAsync();
        await _consumer.CloseAsync();
        Environment.SetEnvironmentVariable(ConnectionStringEnv, null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesEventToHub()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "eventhub-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "eventhub-integration" },
        };

        await _sink.ProduceAsync(envelope);

        var received = await ReceiveEventAsync(messageId);
        received.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(received!.EventBody);
        deserialized.GetProperty("endpointId").GetString().Should().Be("eventhub-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_EventHasApplicationProperties()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "eventhub-attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

        var received = await ReceiveEventAsync(messageId);
        received.Should().NotBeNull();

        received!.MessageId.Should().Be(messageId);
        received.ContentType.Should().Be("application/json");
        received.Properties["hookpipe.message.id"].Should().Be(messageId);
        received.Properties["hookpipe.endpoint.id"].Should().Be("eventhub-attr-test");
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
