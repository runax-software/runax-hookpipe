using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Azure.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Tests.Brokers.Azure.ServiceBus;

/// <summary>
/// Integration tests for <see cref="ServiceBusBroker"/>.
/// Requires Azure Service Bus emulator running on localhost:5673 (docker compose up servicebus-emulator).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ServiceBusBrokerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Endpoint=sb://localhost:5673/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    private const string QueueName = "hookpipe-test";

    private ServiceBusBroker _broker = null!;
    private ServiceBusClient _readerClient = null!;
    private ServiceBusReceiver _receiver = null!;

    public async ValueTask InitializeAsync()
    {
        _readerClient = new ServiceBusClient(ConnectionString);
        _receiver = _readerClient.CreateReceiver(QueueName);
        await WaitForServiceBusAsync();

        var settings = new ServiceBusBrokerSettings { ConnectionString = ConnectionString };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = ServiceBusBroker.Create("test-servicebus", settings, loggerFactory.CreateLogger<ServiceBusBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _receiver.DisposeAsync();
        await _readerClient.DisposeAsync();
    }

    [Fact]
    public async Task ProduceAsync_SendsMessageToQueue()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "servicebus-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "servicebus-integration" },
        };

        await _broker.PublishAsync(QueueName, envelope);

        var received = await ReceiveMessageAsync(messageId);
        received.Should().NotBeNull();
        var message = received!;

        try
        {
            var deserialized = JsonSerializer.Deserialize<JsonElement>(message.Body.ToString());
            deserialized.GetProperty("webhookId").GetString().Should().Be("servicebus-integration-test");
            deserialized.GetProperty("method").GetString().Should().Be("POST");
        }
        finally
        {
            await _receiver.CompleteMessageAsync(message);
        }
    }

    [Fact]
    public async Task ProduceAsync_MessageHasApplicationProperties()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "servicebus-attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(QueueName, envelope);

        var received = await ReceiveMessageAsync(messageId);
        received.Should().NotBeNull();
        var message = received!;

        try
        {
            message.MessageId.Should().Be(messageId);
            message.ContentType.Should().Be("application/json");
            message.ApplicationProperties["hookpipe.message.id"].Should().Be(messageId);
            message.ApplicationProperties["hookpipe.webhook.id"].Should().Be("servicebus-attr-test");
        }
        finally
        {
            await _receiver.CompleteMessageAsync(message);
        }
    }

    private async Task WaitForServiceBusAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await _receiver.PeekMessageAsync();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException("Azure Service Bus emulator did not become ready.", lastException);
    }

    private async Task<ServiceBusReceivedMessage?> ReceiveMessageAsync(string messageId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var messages = await _receiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(2));

            foreach (var message in messages)
            {
                if (message.MessageId == messageId)
                {
                    return message;
                }

                await _receiver.CompleteMessageAsync(message);
            }
        }

        return null;
    }
}
