using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Brokers.Aws.EventBridge;
using Runax.Hookpipe.Brokers.Aws.Sns;
using Runax.Hookpipe.Brokers.Aws.Sqs;
using Runax.Hookpipe.Brokers.Azure.EventHubs;
using Runax.Hookpipe.Brokers.Azure.ServiceBus;
using Runax.Hookpipe.Brokers.Google.PubSub;
using Runax.Hookpipe.Brokers.Http;
using Runax.Hookpipe.Brokers.Kafka;
using Runax.Hookpipe.Brokers.RabbitMq;
using Runax.Hookpipe.Brokers.Redis;
using Runax.Hookpipe.Brokers.Stdout;
using Microsoft.Extensions.Logging;

namespace Runax.Hookpipe.Brokers;

/// <summary>
/// Creates and connects the configured brokers.
/// </summary>
public static class BrokerFactory
{
    /// <summary>
    /// Connects every broker in the configuration and returns the brokers keyed by broker ID.
    /// </summary>
    /// <param name="configuration">The loaded Hookpipe configuration.</param>
    /// <param name="loggerFactory">Logger factory for creating broker-specific loggers.</param>
    /// <returns>Brokers keyed by broker ID, compared case-insensitively.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a broker type is unknown.</exception>
    public static async Task<Dictionary<string, IBroker>> CreateAllAsync(
        HookpipeConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(BrokerFactory));
        var brokers = new Dictionary<string, IBroker>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, broker) in configuration.Brokers)
        {
            logger.LogDebug("[Hookpipe.Broker:{Type}:{Id}] Creating", broker.Type, id);
            brokers[id] = await CreateAsync(id, broker, TopicsFor(configuration, id), loggerFactory);
            logger.LogInformation("[Hookpipe.Broker:{Type}:{Id}] Initialized", broker.Type, id);
        }

        logger.LogInformation("[Hookpipe.Broker] Initialized {Count} broker(s)", brokers.Count);
        return brokers;
    }

    /// <summary>
    /// The topics the configuration's targets use on a broker, so brokers that create a client per topic
    /// can create and health-check them up front. Templated topics are only known per message, so
    /// they're left out.
    /// </summary>
    internal static IReadOnlyCollection<string> TopicsFor(HookpipeConfiguration configuration, string brokerId) =>
        configuration.Webhooks.Values
            .SelectMany(webhook => (webhook.To ?? []).Concat((webhook.Route ?? []).SelectMany(rule => rule.To)))
            .Where(target => target is { Topic: not null, TopicTemplate: null } &&
                             string.Equals(target.Broker, brokerId, StringComparison.OrdinalIgnoreCase))
            .Select(target => target.Topic!)
            .Distinct()
            .ToList();

    private static async Task<IBroker> CreateAsync(
        string id,
        BrokerConfiguration broker,
        IReadOnlyCollection<string> topics,
        ILoggerFactory loggerFactory) =>
        broker.Type switch
        {
            StdoutBroker.TypeName => new StdoutBroker(loggerFactory.CreateLogger<StdoutBroker>()),
            HttpRelayBroker.TypeName => HttpRelayBroker.Create(id, Settings<HttpBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<HttpRelayBroker>()),
            RabbitMqBroker.TypeName => await RabbitMqBroker.CreateAsync(id, Settings<RabbitMqBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<RabbitMqBroker>()),
            KafkaBroker.TypeName => KafkaBroker.Create(id, Settings<KafkaBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<KafkaBroker>()),
            SqsBroker.TypeName => SqsBroker.Create(id, Settings<SqsBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<SqsBroker>()),
            SnsBroker.TypeName => SnsBroker.Create(id, Settings<SnsBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<SnsBroker>()),
            EventBridgeBroker.TypeName => EventBridgeBroker.Create(id, Settings<EventBridgeBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<EventBridgeBroker>()),
            RedisStreamBroker.TypeName => RedisStreamBroker.Create(id, Settings<RedisBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<RedisStreamBroker>()),
            GooglePubSubBroker.TypeName => GooglePubSubBroker.Create(id, Settings<GooglePubSubBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<GooglePubSubBroker>(), topics),
            ServiceBusBroker.TypeName => ServiceBusBroker.Create(id, Settings<ServiceBusBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<ServiceBusBroker>(), topics),
            EventHubsBroker.TypeName => EventHubsBroker.Create(id, Settings<EventHubsBrokerSettings>(id, broker),
                loggerFactory.CreateLogger<EventHubsBroker>(), topics),
            _ => throw new InvalidOperationException($"Unknown broker type '{broker.Type}' for broker '{id}'"),
        };

    private static TSettings Settings<TSettings>(string id, BrokerConfiguration broker) where TSettings : class =>
        broker.Settings as TSettings
        ?? throw new InvalidOperationException(
            $"Broker '{id}' ({broker.Type}) needs settings of type {typeof(TSettings).Name}");
}
