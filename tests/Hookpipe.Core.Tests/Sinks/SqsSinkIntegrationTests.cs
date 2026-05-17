using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Tests.Sinks;

/// <summary>
/// Integration tests for <see cref="SqsSink"/>.
/// Requires LocalStack running on localhost:4566 (docker compose up).
/// </summary>
[Trait("Category", "Integration")]
[Collection("AWS")]
public sealed class SqsSinkIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string ServiceUrl = "http://localhost:4566";
    private const string Region = "us-east-1";
    private const string QueueUrlEnv = "TEST_SQS_QUEUE_URL";
    private const string ServiceUrlEnv = "TEST_AWS_SERVICE_URL";
    private const string RegionEnv = "TEST_AWS_REGION";

    private SqsSink _sink = null!;
    private IAmazonSQS _reader = null!;
    private string _queueUrl = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(ServiceUrlEnv, ServiceUrl);
        Environment.SetEnvironmentVariable(RegionEnv, Region);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");

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

        Environment.SetEnvironmentVariable(QueueUrlEnv, _queueUrl);

        var sinkConfig = new SinkConfig
        {
            Id = "test-sqs",
            Type = "sqs",
            Settings = new Dictionary<string, string>
            {
                ["queue_url_env"] = QueueUrlEnv,
                ["region_env"] = RegionEnv,
                ["service_url_env"] = ServiceUrlEnv,
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _sink = SqsSink.Create(sinkConfig, loggerFactory.CreateLogger<SqsSink>());
    }

    public async Task DisposeAsync()
    {
        await _reader.DeleteQueueAsync(_queueUrl);
    }

    public void Dispose()
    {
        _sink.Dispose();
        _reader.Dispose();
        Environment.SetEnvironmentVariable(QueueUrlEnv, null);
        Environment.SetEnvironmentVariable(ServiceUrlEnv, null);
        Environment.SetEnvironmentVariable(RegionEnv, null);
    }

    [Fact]
    public async Task ProduceAsync_SendsMessageToQueue()
    {
        var envelope = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            EndpointId = "sqs-integration-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
            Body = new { test = "sqs-integration" },
        };

        await _sink.ProduceAsync(envelope);

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
        deserialized.GetProperty("endpointId").GetString().Should().Be("sqs-integration-test");
        deserialized.GetProperty("method").GetString().Should().Be("POST");
    }

    [Fact]
    public async Task ProduceAsync_MessageHasAttributes()
    {
        var messageId = Guid.NewGuid().ToString();
        var envelope = new MessageEnvelope
        {
            Id = messageId,
            EndpointId = "attr-test",
            ReceivedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/test",
            RemoteAddress = "127.0.0.1",
        };

        await _sink.ProduceAsync(envelope);

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
        attrs["hookpipe.endpoint.id"].StringValue.Should().Be("attr-test");
    }
}
