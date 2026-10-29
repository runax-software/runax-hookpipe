using System.Text;
using System.Text.Json;
using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.RabbitMq;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Runax.Hookpipe.Tests.Brokers.RabbitMq;

/// <summary>
/// Integration tests for <see cref="RabbitMqBroker"/>.
/// Requires RabbitMQ running on localhost:5672 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqBrokerIntegrationTests : IAsyncLifetime
{
    private const string Exchange = "hookpipe-test";
    private const string RoutingKey = "test-events";

    private readonly string _queueName = $"hookpipe-test-queue-{Guid.NewGuid():N}";

    private RabbitMqBroker _broker = null!;
    private IConnection _consumerConnection = null!;
    private IChannel _consumerChannel = null!;

    public async ValueTask InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable("RABBITMQ_URL") ?? "amqp://guest:guest@localhost:5672";

        var settings = new RabbitMqBrokerSettings { Uri = url, ExchangeName = Exchange };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = await RabbitMqBroker.CreateAsync("test-rabbitmq", settings, loggerFactory.CreateLogger<RabbitMqBroker>());

        // Set up a consumer queue bound to one routing key to verify messages
        var factory = new ConnectionFactory { Uri = new Uri(url) };
        _consumerConnection = await factory.CreateConnectionAsync();
        _consumerChannel = await _consumerConnection.CreateChannelAsync();

        await _consumerChannel.QueueDeclareAsync(_queueName, durable: false, exclusive: true, autoDelete: true);
        await _consumerChannel.QueueBindAsync(_queueName, Exchange, RoutingKey);
    }

    public async ValueTask DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _consumerChannel.CloseAsync();
        await _consumerConnection.CloseAsync();
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToExchange()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "rabbitmq-integration" },
        };

        await _broker.PublishAsync(RoutingKey, envelope);

        // Give RabbitMQ a moment to deliver
        await Task.Delay(500);

        var result = await _consumerChannel.BasicGetAsync(_queueName, autoAck: true);
        result.Should().NotBeNull();

        var body = Encoding.UTF8.GetString(result!.Body.ToArray());
        var deserialized = JsonSerializer.Deserialize<JsonElement>(body);
        deserialized.GetProperty("webhookId").GetString().Should().Be("integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasCorrectProperties()
    {
        var envelope = new MessageEnvelope
        {
            Id = "test-msg-123",
            WebhookId = "prop-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(RoutingKey, envelope);
        await Task.Delay(500);

        var result = await _consumerChannel.BasicGetAsync(_queueName, autoAck: true);
        result.Should().NotBeNull();
        result!.BasicProperties.ContentType.Should().Be("application/json");
        result.BasicProperties.MessageId.Should().Be("test-msg-123");
        result.RoutingKey.Should().Be(RoutingKey);
    }

    [Fact]
    public async Task ProduceAsync_OtherTopic_NotRoutedToBoundQueue()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "other-topic-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync("other-events", envelope);
        await Task.Delay(500);

        var result = await _consumerChannel.BasicGetAsync(_queueName, autoAck: true);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ProduceAsync_NoTopic_ThrowsArgumentException(string? topic)
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "no-topic-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        var act = () => _broker.PublishAsync(topic, envelope);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*needs a topic*");
    }
}
