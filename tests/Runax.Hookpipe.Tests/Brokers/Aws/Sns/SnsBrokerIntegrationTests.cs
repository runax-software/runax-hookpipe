using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using Runax.Hookpipe.Models;
using Runax.Hookpipe.Brokers.Aws.Sns;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Aws.Sns;

/// <summary>
/// Integration tests for <see cref="SnsBroker"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// Creates an SNS topic + SQS queue subscription to verify messages.
/// </summary>
[Trait("Category", "Integration")]
[Collection("AWS")]
public sealed class SnsBrokerIntegrationTests : IAsyncLifetime
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string Topic = "orders";

    private SnsBroker _broker = null!;
    private IAmazonSimpleNotificationService _snsClient = null!;
    private IAmazonSQS _sqsClient = null!;
    private string _topicArn = null!;
    private string _queueUrl = null!;

    public async ValueTask InitializeAsync()
    {
        var snsConfig = new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        _snsClient = new AmazonSimpleNotificationServiceClient("test", "test", snsConfig);

        // Create topic
        var topicName = $"hookpipe-test-{Guid.NewGuid():N}";
        var topicResponse = await _snsClient.CreateTopicAsync(topicName);
        _topicArn = topicResponse.TopicArn;

        // Create SQS queue to subscribe and verify messages
        var sqsConfig = new AmazonSQSConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        _sqsClient = new AmazonSQSClient("test", "test", sqsConfig);

        var queueName = $"hookpipe-test-{Guid.NewGuid():N}";
        var queueResponse = await _sqsClient.CreateQueueAsync(queueName);
        _queueUrl = queueResponse.QueueUrl;

        // Subscribe SQS to SNS
        await _snsClient.SubscribeQueueAsync(_topicArn, _sqsClient, _queueUrl);

        // Create broker
        var settings = new SnsBrokerSettings
        {
            Region = Region,
            ServiceUrl = ServiceUrl,
            TopicArnMap = new() { [Topic] = _topicArn },
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _broker = SnsBroker.Create("test-sns", settings, loggerFactory.CreateLogger<SnsBroker>());
    }

    public async ValueTask DisposeAsync()
    {
        await _sqsClient.DeleteQueueAsync(_queueUrl);
        await _snsClient.DeleteTopicAsync(_topicArn);
        _broker.Dispose();
        _sqsClient.Dispose();
        _snsClient.Dispose();
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToTopic()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            WebhookId = "sns-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "sns-integration" },
        };

        await _broker.PublishAsync(Topic, envelope);

        // Read from SQS subscription
        var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5,
        });

        response.Messages.Count.ShouldBe(1);

        // SNS wraps the message in an envelope — parse the SNS wrapper first
        var snsWrapper = JsonSerializer.Deserialize<JsonElement>(response.Messages[0].Body);
        var messageBody = snsWrapper.GetProperty("Message").GetString();
        messageBody.ShouldNotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(messageBody!);
        deserialized.GetProperty("webhookId").GetString().ShouldBe("sns-integration-test");
        deserialized.GetProperty("method").GetString().ShouldBe("POST");
    }
}
