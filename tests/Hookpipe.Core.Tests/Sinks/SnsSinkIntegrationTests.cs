using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="SnsSink"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// Creates an SNS topic + SQS queue subscription to verify messages.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SnsSinkIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string TopicArnEnv = "TEST_SNS_TOPIC_ARN";
    private const string ServiceUrlEnv = "TEST_AWS_SERVICE_URL";
    private const string RegionEnv = "TEST_AWS_REGION";

    private SnsSink _sink = null!;
    private IAmazonSQS _sqsClient = null!;
    private string _topicArn = null!;
    private string _queueUrl = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(ServiceUrlEnv, ServiceUrl);
        Environment.SetEnvironmentVariable(RegionEnv, Region);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");

        var snsConfig = new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = ServiceUrl,
            AuthenticationRegion = Region,
        };
        var snsClient = new AmazonSimpleNotificationServiceClient("test", "test", snsConfig);

        // Create topic
        var topicName = $"hookpipe-test-{Guid.NewGuid():N}";
        var topicResponse = await snsClient.CreateTopicAsync(topicName);
        _topicArn = topicResponse.TopicArn;
        Environment.SetEnvironmentVariable(TopicArnEnv, _topicArn);

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
        await snsClient.SubscribeQueueAsync(_topicArn, _sqsClient, _queueUrl);

        // Create sink
        var sinkConfig = new SinkConfig
        {
            Id = "test-sns",
            Type = "sns",
            Settings = new Dictionary<string, string>
            {
                ["topic_arn_env"] = TopicArnEnv,
                ["region_env"] = RegionEnv,
                ["service_url_env"] = ServiceUrlEnv,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = SnsSink.Create(sinkConfig, loggerFactory.CreateLogger<SnsSink>());
    }

    public async Task DisposeAsync()
    {
        await _sqsClient.DeleteQueueAsync(_queueUrl);
    }

    public void Dispose()
    {
        _sink.Dispose();
        _sqsClient.Dispose();
        Environment.SetEnvironmentVariable(TopicArnEnv, null);
        Environment.SetEnvironmentVariable(ServiceUrlEnv, null);
        Environment.SetEnvironmentVariable(RegionEnv, null);
    }

    [Fact]
    public async Task ProduceAsync_PublishesMessageToTopic()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "sns-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "sns-integration" },
        };

        await _sink.ProduceAsync(envelope);

        // Read from SQS subscription
        var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5,
        });

        response.Messages.Should().HaveCount(1);

        // SNS wraps the message in an envelope — parse the SNS wrapper first
        var snsWrapper = JsonSerializer.Deserialize<JsonElement>(response.Messages[0].Body);
        var messageBody = snsWrapper.GetProperty("Message").GetString();
        messageBody.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<JsonElement>(messageBody!);
        deserialized.GetProperty("endpointId").GetString().Should().Be("sns-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }
}
