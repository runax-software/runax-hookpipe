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

namespace Runax.Hookpipe.Brokers;

/// <summary>
/// The broker types Hookpipe knows.
/// </summary>
internal static class BrokerTypes
{
    /// <summary>
    /// Every known broker type, keyed by its configuration name.
    /// </summary>
    public static IReadOnlyDictionary<string, BrokerType> All { get; } = new[]
    {
        new BrokerType(StdoutBroker.TypeName, null, TopicUse.None),
        new BrokerType(HttpRelayBroker.TypeName, typeof(HttpBrokerSettings), TopicUse.Optional),
        new BrokerType(RabbitMqBroker.TypeName, typeof(RabbitMqBrokerSettings), TopicUse.Required),
        new BrokerType(KafkaBroker.TypeName, typeof(KafkaBrokerSettings), TopicUse.Required),
        new BrokerType(SqsBroker.TypeName, typeof(SqsBrokerSettings), TopicUse.Required),
        new BrokerType(SnsBroker.TypeName, typeof(SnsBrokerSettings), TopicUse.Required),
        new BrokerType(EventBridgeBroker.TypeName, typeof(EventBridgeBrokerSettings), TopicUse.Required),
        new BrokerType(RedisStreamBroker.TypeName, typeof(RedisBrokerSettings), TopicUse.Required),
        new BrokerType(GooglePubSubBroker.TypeName, typeof(GooglePubSubBrokerSettings), TopicUse.Required),
        new BrokerType(ServiceBusBroker.TypeName, typeof(ServiceBusBrokerSettings), TopicUse.Required),
        new BrokerType(EventHubsBroker.TypeName, typeof(EventHubsBrokerSettings), TopicUse.Required),
    }.ToDictionary(type => type.Name);
}
