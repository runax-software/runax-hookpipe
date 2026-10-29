using System.Text.Json;
using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Runax.Hookpipe.Tests.Brokers.Redis;

/// <summary>
/// Integration tests for <see cref="RedisStreamBroker"/>.
/// Requires Redis running on localhost:6379 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisStreamBrokerIntegrationTests : IDisposable
{
    private const string Connection = "localhost:6379";

    private readonly string _streamKey = $"hookpipe-test-{Guid.NewGuid():N}";
    private readonly RedisStreamBroker _broker;
    private readonly IConnectionMultiplexer _reader;

    public RedisStreamBrokerIntegrationTests()
    {
        var settings = new RedisBrokerSettings { Configuration = Connection };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = RedisStreamBroker.Create("test-redis", settings, loggerFactory.CreateLogger<RedisStreamBroker>());
        _reader = ConnectionMultiplexer.Connect(Connection);
    }

    public void Dispose()
    {
        _reader.GetDatabase().KeyDelete(_streamKey);
        _broker.Dispose();
        _reader.Dispose();
    }

    [Fact]
    public async Task ProduceAsync_AppendsMessageToStream()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "redis-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "redis-integration" },
        };

        await _broker.PublishAsync(_streamKey, envelope);

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().NotBeEmpty();

        var entry = entries[0];
        var payload = entry.Values.First(v => v.Name == "payload").Value.ToString();
        var deserialized = JsonSerializer.Deserialize<JsonElement>(payload);
        deserialized.GetProperty("webhookId").GetString().Should().Be("redis-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasHookpipeFields()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "field-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(_streamKey, envelope);

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().NotBeEmpty();

        var entry = entries[0];
        entry.Values.First(v => v.Name == "hookpipe.message.id").Value.ToString().Should().Be(messageId);
        entry.Values.First(v => v.Name == "hookpipe.webhook.id").Value.ToString().Should().Be("field-test");
    }

    [Fact]
    public async Task ProduceAsync_MultipleMessages_AllAppended()
    {
        for (var i = 0; i < 3; i++)
        {
            var envelope = new MessageEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                WebhookId = $"batch-test-{i}",
                ReceivedAt = DateTimeOffset.UtcNow,
                Method = "POST",
                Path = "/test",
                RemoteAddress = "127.0.0.1",
            };

            await _broker.PublishAsync(_streamKey, envelope);
        }

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().HaveCount(3);
    }
}
