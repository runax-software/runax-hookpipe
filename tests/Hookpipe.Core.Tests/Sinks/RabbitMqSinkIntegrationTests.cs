using System.Text;
using System.Text.Json;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="RabbitMqSink"/>.
/// Requires RabbitMQ running on localhost:5672 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqSinkIntegrationTests : IAsyncLifetime
{
    private const string Exchange = "hookpipe-test";
    private const string RoutingKey = "test-events";
    private const string QueueName = "hookpipe-test-queue";

    private RabbitMqSink _sink = null!;
    private IConnection _consumerConnection = null!;
    private IChannel _consumerChannel = null!;

    public async ValueTask InitializeAsync()
    {
        var url = Environment.GetEnvironmentVariable("RABBITMQ_URL") ?? "amqp://guest:guest@localhost:5672";
        Environment.SetEnvironmentVariable("TEST_RABBITMQ_URL", url);

        var sinkConfig = new SinkConfig
        {
            Id = "test-rabbitmq",
            Type = "rabbitmq",
            Settings = new Dictionary<string, string>
            {
                ["url_env"] = "TEST_RABBITMQ_URL",
                ["exchange"] = Exchange,
                ["routing_key"] = RoutingKey,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = await RabbitMqSink.CreateAsync(sinkConfig, loggerFactory.CreateLogger<RabbitMqSink>());

        // Set up a consumer to verify messages
        var factory = new ConnectionFactory { Uri = new Uri(url) };
        _consumerConnection = await factory.CreateConnectionAsync();
        _consumerChannel = await _consumerConnection.CreateChannelAsync();

        await _consumerChannel.QueueDeclareAsync(QueueName, durable: false, exclusive: false, autoDelete: true);
        await _consumerChannel.QueueBindAsync(QueueName, Exchange, RoutingKey);
    }

    public async ValueTask DisposeAsync()
    {
        await _sink.DisposeAsync();
        await _consumerChannel.CloseAsync();
        await _consumerConnection.CloseAsync();
        Environment.SetEnvironmentVariable("TEST_RABBITMQ_URL", null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToExchange()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "rabbitmq-integration" },
        };

        await _sink.ProduceAsync(envelope);

        // Give RabbitMQ a moment to deliver
        await Task.Delay(500);

        var result = await _consumerChannel.BasicGetAsync(QueueName, autoAck: true);
        result.Should().NotBeNull();

        var body = Encoding.UTF8.GetString(result!.Body.ToArray());
        var deserialized = JsonSerializer.Deserialize<JsonElement>(body);
        deserialized.GetProperty("endpointId").GetString().Should().Be("integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasCorrectProperties()
    {
        var envelope = new MessageEnvelope
        {
            Id = "test-msg-123",
            EndpointId = "prop-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);
        await Task.Delay(500);

        var result = await _consumerChannel.BasicGetAsync(QueueName, autoAck: true);
        result.Should().NotBeNull();
        result!.BasicProperties.ContentType.Should().Be("application/json");
        result.BasicProperties.MessageId.Should().Be("test-msg-123");
    }
}
