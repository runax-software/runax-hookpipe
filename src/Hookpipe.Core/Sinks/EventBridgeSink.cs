using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Hookpipe.Core.Config;
using Hookpipe.Core.Models;
using Hookpipe.Core.Sinks.Health;
using Microsoft.Extensions.Logging;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Sink that puts message envelopes to an AWS EventBridge event bus.
/// Settings: event_bus_env, source, detail_type, region_env, service_url_env (from <see cref="SinkConfig.Settings"/>).
/// </summary>
public sealed class EventBridgeSink : ISink, ISinkHealthCheck, IDisposable
{
    /// <summary>
    /// The sink type identifier.
    /// </summary>
    public const string TypeName = "eventbridge";

    private readonly ILogger<EventBridgeSink> _logger;
    private readonly IAmazonEventBridge _client;
    private readonly string _eventBus;
    private readonly string _source;
    private readonly string _detailType;
    private readonly string _sinkId;

    private EventBridgeSink(
        ILogger<EventBridgeSink> logger,
        IAmazonEventBridge client,
        string eventBus,
        string source,
        string detailType,
        string sinkId)
    {
        _logger = logger;
        _client = client;
        _eventBus = eventBus;
        _source = source;
        _detailType = detailType;
        _sinkId = sinkId;
    }

    /// <inheritdoc />
    public string Type => TypeName;

    /// <summary>
    /// Creates a new EventBridge sink from the given config settings.
    /// </summary>
    /// <param name="sinkConfig">Sink configuration containing event bus and source settings.</param>
    /// <param name="logger">Logger for this sink instance.</param>
    /// <returns>A configured <see cref="EventBridgeSink"/> ready to put events.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the event bus env var is not set.</exception>
    public static EventBridgeSink Create(SinkConfig sinkConfig, ILogger<EventBridgeSink> logger)
    {
        var eventBus = SinkHelper.RequireEnvVar(sinkConfig, "event_bus_env", "EVENTBRIDGE_BUS_NAME");
        var region = SinkHelper.OptionalEnvVar(sinkConfig, "region_env", "AWS_REGION");
        var serviceUrl = SinkHelper.OptionalEnvVar(sinkConfig, "service_url_env", "AWS_SERVICE_URL");
        var source = sinkConfig.Settings.GetValueOrDefault("source", "hookpipe");
        var detailType = sinkConfig.Settings.GetValueOrDefault("detail_type", "webhook");

        var config = new AmazonEventBridgeConfig();
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = region ?? "us-east-1";
        }
        else if (!string.IsNullOrEmpty(region))
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);

        logger.LogInformation(
            "[Hookpipe.Sink:eventbridge:{SinkId}] Configured for bus '{EventBus}', source='{Source}', detail_type='{DetailType}'",
            sinkConfig.Id, eventBus, source, detailType);

        var client = !string.IsNullOrEmpty(serviceUrl)
            ? new AmazonEventBridgeClient(
                new Amazon.Runtime.BasicAWSCredentials(
                    Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "test",
                    Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "test"),
                config)
            : new AmazonEventBridgeClient(config);

        return new EventBridgeSink(logger, client, eventBus, source, detailType, sinkConfig.Id);
    }

    /// <inheritdoc />
    public async Task ProduceAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, SinkHelper.JsonOptions);

        var request = new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = _eventBus,
                    Source = _source,
                    DetailType = _detailType,
                    Detail = json,
                }
            ]
        };

        var response = await _client.PutEventsAsync(request, cancellationToken);

        if (response.FailedEntryCount > 0)
        {
            _logger.LogWarning(
                "[Hookpipe.Sink:eventbridge:{SinkId}] Failed to put event '{MessageId}': {Error}",
                _sinkId, message.Id, response.Entries[0].ErrorMessage);
            throw new InvalidOperationException(
                $"EventBridge put failed: {response.Entries[0].ErrorCode} - {response.Entries[0].ErrorMessage}");
        }

        _logger.LogDebug(
            "[Hookpipe.Sink:eventbridge:{SinkId}] Put event '{MessageId}', EventId='{EventId}'",
            _sinkId, message.Id, response.Entries[0].EventId);
    }

    /// <inheritdoc />
    public async Task<SinkHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DescribeEventBusAsync(
                new DescribeEventBusRequest { Name = _eventBus }, cancellationToken);
            return new SinkHealth(SinkHealthStatus.Healthy);
        }
        catch (Exception ex)
        {
            return new SinkHealth(SinkHealthStatus.Unhealthy, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
