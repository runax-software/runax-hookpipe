using System.Text.Json;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="RedisStreamSink"/>.
/// Requires Redis running on localhost:6379 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RedisStreamSinkIntegrationTests : IDisposable
{
    private const string Connection = "localhost:6379";

    private readonly string _streamKey = $"hookpipe-test-{Guid.NewGuid():N}";
    private readonly RedisStreamSink _sink;
    private readonly IConnectionMultiplexer _reader;

    public RedisStreamSinkIntegrationTests()
    {
        Environment.SetEnvironmentVariable("TEST_REDIS_CONNECTION", Connection);

        var sinkConfig = new SinkConfig
        {
            Id = "test-redis",
            Type = "redis-stream",
            Settings = new Dictionary<string, string>
            {
                ["connection_env"] = "TEST_REDIS_CONNECTION",
                ["stream_key"] = _streamKey,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = RedisStreamSink.Create(sinkConfig, loggerFactory.CreateLogger<RedisStreamSink>());
        _reader = ConnectionMultiplexer.Connect(Connection);
    }

    public void Dispose()
    {
        _reader.GetDatabase().KeyDelete(_streamKey);
        _sink.Dispose();
        _reader.Dispose();
        Environment.SetEnvironmentVariable("TEST_REDIS_CONNECTION", null);
    }

    [Fact]
    public async Task ProduceAsync_AppendsMessageToStream()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "redis-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "redis-integration" },
        };

        await _sink.ProduceAsync(envelope);

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().NotBeEmpty();

        var entry = entries[0];
        var payload = entry.Values.First(v => v.Name == "payload").Value.ToString();
        var deserialized = JsonSerializer.Deserialize<JsonElement>(payload);
        deserialized.GetProperty("endpointId").GetString().Should().Be("redis-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasHookpipeFields()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "field-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().NotBeEmpty();

        var entry = entries[0];
        entry.Values.First(v => v.Name == "hookpipe.message.id").Value.ToString().Should().Be(messageId);
        entry.Values.First(v => v.Name == "hookpipe.endpoint.id").Value.ToString().Should().Be("field-test");
    }

    [Fact]
    public async Task ProduceAsync_MultipleMessages_AllAppended()
    {
        for (var i = 0; i < 3; i++)
        {
            var envelope = new MessageEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                EndpointId = $"batch-test-{i}",
                ReceivedAt = DateTimeOffset.UtcNow,
                Method = "POST",
                Path = "/test",
                RemoteAddress = "127.0.0.1",
            };

            await _sink.ProduceAsync(envelope);
        }

        var entries = await _reader.GetDatabase().StreamReadAsync(_streamKey, "0-0");
        entries.Should().HaveCount(3);
    }
}
