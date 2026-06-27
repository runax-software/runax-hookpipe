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
public sealed class KafkaSinkIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string Brokers = "localhost:9092";

    private readonly string _topic = $"hookpipe-test-{Guid.NewGuid():N}";
    private KafkaSink _sink = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TEST_KAFKA_BROKERS", Brokers);

        // Wait for Kafka to be fully ready
        using var adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Brokers }).Build();
        for (var i = 0; i < 30; i++)
        {
            try
            {
                adminClient.GetMetadata(TimeSpan.FromSeconds(5));
                break;
            }
            catch
            {
                await Task.Delay(2000);
            }
        }

        var sinkConfig = new SinkConfig
        {
            Id = "test-kafka",
            Type = "kafka",
            Settings = new Dictionary<string, string>
            {
                ["brokers_env"] = "TEST_KAFKA_BROKERS",
                ["topic"] = _topic,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = KafkaSink.Create(sinkConfig, loggerFactory.CreateLogger<KafkaSink>());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        _sink.Dispose();
        Environment.SetEnvironmentVariable("TEST_KAFKA_BROKERS", null);
    }

    private ConsumeResult<string, string>? ConsumeWithRetry(IConsumer<string, string> consumer, int timeoutSeconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(5));
            if (result is not null)
                return result;
        }

        return null;
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

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
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

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
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

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
        result.Should().NotBeNull();

        var headers = result!.Message.Headers;
        var msgIdHeader = headers.GetLastBytes("hookpipe.message.id");
        var endpointHeader = headers.GetLastBytes("hookpipe.endpoint.id");

        System.Text.Encoding.UTF8.GetString(msgIdHeader).Should().Be(messageId);
        System.Text.Encoding.UTF8.GetString(endpointHeader).Should().Be("header-test");
    }
}
