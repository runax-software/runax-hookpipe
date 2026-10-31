using Microsoft.Extensions.Logging.Abstractions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Hosting;
using Shouldly;

namespace Runax.Hookpipe.Tests.Hosting;

public sealed class RetryPipelinesTests
{
    [Theory]
    [InlineData(RetryBackoff.Constant, 0, 2)]
    [InlineData(RetryBackoff.Constant, 3, 2)]
    [InlineData(RetryBackoff.Linear, 0, 2)]
    [InlineData(RetryBackoff.Linear, 2, 6)]
    [InlineData(RetryBackoff.Exponential, 0, 2)]
    [InlineData(RetryBackoff.Exponential, 2, 18)]
    [InlineData(RetryBackoff.Exponential, 5, 30)]
    public void DelayFor_WithoutJitter_FollowsTheBackoff(RetryBackoff backoff, int attempt, double expectedSeconds)
    {
        var retry = new RetryConfiguration { Backoff = backoff, Factor = 3, Jitter = false };

        RetryPipelines.DelayFor(retry, attempt, jitterSample: 0.99).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(0.0, 1.5)]
    [InlineData(0.5, 2.0)]
    [InlineData(0.999, 2.499)]
    public void DelayFor_WithJitter_SpreadsTheDelay(double sample, double expectedSeconds)
    {
        var retry = new RetryConfiguration { Backoff = RetryBackoff.Constant };

        RetryPipelines.DelayFor(retry, 0, sample).TotalSeconds.ShouldBe(expectedSeconds, 0.001);
    }

    [Fact]
    public void Get_NoRetriesConfigured_ReturnsNull()
    {
        var pipelines = new RetryPipelines(NullLoggerFactory.Instance);

        pipelines.Get("console", null).ShouldBeNull();
        pipelines.Get("console", new RetryConfiguration { MaxRetries = 0 }).ShouldBeNull();
    }

    [Fact]
    public void Get_SamePolicy_ReturnsTheSamePipeline()
    {
        var pipelines = new RetryPipelines(NullLoggerFactory.Instance);
        var retry = new RetryConfiguration();

        pipelines.Get("rabbit", retry).ShouldBeSameAs(pipelines.Get("rabbit", retry));
        pipelines.Get("rabbit", new RetryConfiguration()).ShouldNotBeSameAs(pipelines.Get("rabbit", retry));
    }
}
