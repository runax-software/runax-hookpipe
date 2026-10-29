using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.Sqs;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Tests.Brokers.Aws.Sqs;

/// <summary>
/// Integration tests for <see cref="SqsBroker"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
[Collection("AWS")]
public sealed class SqsBrokerIntegrationTests : IAsyncLifetime
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string Topic = "orders";

    private SqsBroker _broker = null!;
    private IAmazonSQS _reader = null!;
    private string _queueUrl = null!;

    public async ValueTask InitializeAsync()
    {
        var readerConfig = new AmazonSQSConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        _reader = new AmazonSQSClient("test", "test", readerConfig);

        // Create a test queue
        var queueName = $"hookpipe-test-{Guid.NewGuid():N}";
        var createResponse = await _reader.CreateQueueAsync(queueName);
        _queueUrl = createResponse.QueueUrl;

        var settings = new SqsBrokerSettings
        {
            Region = Region,
            ServiceUrl = ServiceUrl,
            TopicQueueUrlMap = new() { [Topic] = _queueUrl },
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = SqsBroker.Create("test-sqs", settings, loggerFactory.CreateLogger<SqsBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DeleteQueueAsync(_queueUrl);
        _broker.Dispose();
        _reader.Dispose();
    }

    [Fact]
    public async Task ProduceAsync_SendsMessageToQueue()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "sqs-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "sqs-integration" },
        };

        await _broker.PublishAsync(Topic, envelope);

        var response = await _reader.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5,
            MessageAttributeNames = ["All"],
        });

        response.Messages.Should().HaveCount(1);

        var body = response.Messages[0].Body;
        var deserialized = JsonSerializer.Deserialize<JsonElement>(body);
        deserialized.GetProperty("webhookId").GetString().Should().Be("sqs-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasAttributes()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            WebhookId = "attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _broker.PublishAsync(Topic, envelope);

        var response = await _reader.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5,
            MessageAttributeNames = ["All"],
        });

        response.Messages.Should().HaveCount(1);
        var attrs = response.Messages[0].MessageAttributes;
        attrs["hookpipe.message.id"].StringValue.Should().Be(messageId);
        attrs["hookpipe.webhook.id"].StringValue.Should().Be("attr-test");
    }
}
