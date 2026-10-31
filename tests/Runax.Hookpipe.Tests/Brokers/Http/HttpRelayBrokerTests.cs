using Runax.Hookpipe.Brokers.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Brokers.Http;

public sealed class HttpRelayBrokerTests
{
    [Fact]
    public void Type_ReturnsHttp()
    {
        using var broker = HttpRelayBroker.Create(
            "test",
            new HttpBrokerSettings { Url = "http://localhost:9999" },
            Substitute.For<ILogger<HttpRelayBroker>>());

        broker.Type.ShouldBe("http");
    }

    [Fact]
    public void Create_CustomTimeout_DoesNotThrow()
    {
        var act = () => HttpRelayBroker.Create(
            "test",
            new HttpBrokerSettings { Url = "http://localhost:9999", Timeout = TimeSpan.FromSeconds(5) },
            Substitute.For<ILogger<HttpRelayBroker>>());

        Should.NotThrow(act).Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UrlFor_NoTopic_ReturnsBaseUrl(string? topic)
    {
        var url = HttpRelayBroker.UrlFor("http://relay.local/hooks?token=abc", topic);

        url.ShouldBe("http://relay.local/hooks?token=abc");
    }

    [Theory]
    [InlineData("http://relay.local/hooks", "http://relay.local/hooks/orders")]
    [InlineData("http://relay.local/hooks/", "http://relay.local/hooks/orders")]
    [InlineData("http://relay.local", "http://relay.local/orders")]
    [InlineData("http://relay.local:8080/hooks", "http://relay.local:8080/hooks/orders")]
    public void UrlFor_Topic_AppendsTopicAsPathSegment(string baseUrl, string expected)
    {
        var url = HttpRelayBroker.UrlFor(baseUrl, "orders");

        url.ShouldBe(expected);
    }

    [Fact]
    public void UrlFor_BaseUrlWithQuery_KeepsQueryString()
    {
        var url = HttpRelayBroker.UrlFor("http://relay.local/hooks?token=abc&env=dev", "github.push");

        url.ShouldBe("http://relay.local/hooks/github.push?token=abc&env=dev");
    }

    [Fact]
    public void UrlFor_TopicWithSlash_EscapesSlash()
    {
        var url = HttpRelayBroker.UrlFor("http://relay.local/hooks", "a/b");

        url.ShouldBe("http://relay.local/hooks/a%2Fb");
    }
}
