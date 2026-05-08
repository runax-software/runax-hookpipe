using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="KafkaSink"/>.
/// Requires Kafka running on localhost:9092 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaSinkIntegrationTests : IDisposable
{
    private const string Topic = "hookpipe-integration-test";
    private const string Brokers = "localhost:9092";

    private readonly KafkaSink _sink;
    private readonly IConsumer<string, string> _consumer;

    public KafkaSinkIntegrationTests()
    {
        Environment.SetEnvironmentVariable("TEST_KAFKA_BROKERS", Brokers);

        var sinkConfig = new SinkConfig
        {
            Id = "test-kafka",
            Type = "kafka",
            Settings = new Dictionary<string, string>
            {
                ["brokers_env"] = "TEST_KAFKA_BROKERS",
                ["topic"] = Topic,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = KafkaSink.Create(sinkConfig, loggerFactory.CreateLogger<KafkaSink>());

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"hookpipe-test-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _consumer.Subscribe(Topic);

        // Poll once to trigger partition assignment
        _consumer.Consume(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _sink.Dispose();
        _consumer.Close();
        _consumer.Dispose();
        Environment.SetEnvironmentVariable("TEST_KAFKA_BROKERS", null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToTopic()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "kafka-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "kafka-integration" },
        };

        await _sink.ProduceAsync(envelope);

        var result = _consumer.Consume(TimeSpan.FromSeconds(10));
        result.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(result!.Message.Value);
        deserialized.GetProperty("endpointId").GetString().Should().Be("kafka-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageKeyIsEndpointId()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "key-test-endpoint",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

        var result = _consumer.Consume(TimeSpan.FromSeconds(10));
        result.Should().NotBeNull();
        result!.Message.Key.Should().Be("key-test-endpoint");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasHookpipeHeaders()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "header-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

        var result = _consumer.Consume(TimeSpan.FromSeconds(10));
        result.Should().NotBeNull();

        var headers = result!.Message.Headers;
        var msgIdHeader = headers.GetLastBytes("hookpipe.message.id");
        var endpointHeader = headers.GetLastBytes("hookpipe.endpoint.id");

        System.Text.Encoding.UTF8.GetString(msgIdHeader).Should().Be(messageId);
        System.Text.Encoding.UTF8.GetString(endpointHeader).Should().Be("header-test");
    }
}
