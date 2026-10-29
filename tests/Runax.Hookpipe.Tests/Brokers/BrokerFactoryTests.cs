using FluentAssertions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Brokers;

namespace Runax.Hookpipe.Tests.Brokers;

public sealed class BrokerFactoryTests
{
    [Fact]
    public void TopicsFor_CollectsEachTopicTheBrokersTargetsUse()
    {
        var configuration = ConfigurationLoader.LoadFromString("""
            version: 2
            brokers:
              hubs:
                type: azure-eventhubs
                settings:
                  connection_string: Endpoint=sb://test/;SharedAccessKeyName=a;SharedAccessKey=b
              console:
                type: stdout
            webhooks:
              a:
                to: [hubs:orders, console]
              b:
                route:
                  - when: { header: X-Kind, eq: refund }
                    to: HUBS:refunds
                  - to: hubs:orders
            """);

        BrokerFactory.TopicsFor(configuration, "hubs").Should().BeEquivalentTo("orders", "refunds");
        BrokerFactory.TopicsFor(configuration, "console").Should().BeEmpty();
    }
}
