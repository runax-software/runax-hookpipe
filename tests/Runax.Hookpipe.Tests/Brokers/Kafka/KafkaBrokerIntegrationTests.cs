using System.Text.Json;
using Confluent.Kafka;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Kafka;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Kafka;

/// <summary>
/// Integration tests for <see cref="KafkaBroker"/>.
/// Requires Kafka running on localhost:9092 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaBrokerIntegrationTests : IAsyncLifetime
{
    private const string Brokers = "localhost:9092";

    private readonly string _topic = $"hookpipe-test-{Guid.NewGuid():N}";
    private KafkaBroker _broker = null!;

    public async ValueTask InitializeAsync()
    {
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

        var settings = new KafkaBrokerSettings { BootstrapServers = Brokers };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = KafkaBroker.Create("test-kafka", settings, loggerFactory.CreateLogger<KafkaBroker>());
    }

    public ValueTask DisposeAsync()
    {
        _broker.Dispose();
        return ValueTask.CompletedTask;
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
            WebhookId = "kafka-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "kafka-integration" },
        };

        await _broker.PublishAsync(_topic, envelope);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
        result.ShouldNotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(result!.Message.Value);
        deserialized.GetProperty("webhookId").GetString().ShouldBe("kafka-integration-test");
        deserialized.GetProperty("method").GetString().ShouldBe("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageKeyIsWebhookId()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "key-test-webhook",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(_topic, envelope);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
        result.ShouldNotBeNull();
        result!.Message.Key.ShouldBe("key-test-webhook");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasHookpipeHeaders()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "header-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(_topic, envelope);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Brokers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(_topic);

        var result = ConsumeWithRetry(consumer);
        result.ShouldNotBeNull();

        var headers = result!.Message.Headers;
        var msgIdHeader = headers.GetLastBytes("hookpipe.message.id");
        var webhookHeader = headers.GetLastBytes("hookpipe.webhook.id");

        System.Text.Encoding.UTF8.GetString(msgIdHeader).ShouldBe(messageId);
        System.Text.Encoding.UTF8.GetString(webhookHeader).ShouldBe("header-test");
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

        var exception = await Should.ThrowAsync<ArgumentException>(act);
        exception.Message.ShouldContain("needs a topic");
    }
}
