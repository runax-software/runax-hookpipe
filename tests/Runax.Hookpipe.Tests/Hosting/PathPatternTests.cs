using System.Text.RegularExpressions;
using FluentAssertions;
using Runax.Hookpipe.Hosting;

namespace Runax.Hookpipe.Tests.Hosting;

public sealed class PathPatternTests
{
    [Fact]
    public void BuildPathPattern_NoParameters_MatchesPathExactly()
    {
        var (pattern, paramNames) = HookpipeEndpointRouteBuilderExtensions.BuildPathPattern("/github/push");

        paramNames.Should().BeEmpty();
        Regex.IsMatch("/github/push", pattern).Should().BeTrue();
        Regex.IsMatch("/github/push/extra", pattern).Should().BeFalse();
    }

    [Fact]
    public void BuildPathPattern_SeveralParameters_CapturesEachInOrder()
    {
        var (pattern, paramNames) =
            HookpipeEndpointRouteBuilderExtensions.BuildPathPattern("/ingest/{source}/events/{kind}");

        var match = Regex.Match("/ingest/github/events/push", pattern);

        paramNames.Should().Equal("source", "kind");
        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("github");
        match.Groups[2].Value.Should().Be("push");
    }

    [Fact]
    public void BuildPathPattern_RegexCharactersInLiteral_AreEscaped()
    {
        var (pattern, _) = HookpipeEndpointRouteBuilderExtensions.BuildPathPattern("/v1.0/{source}");

        Regex.IsMatch("/v1.0/github", pattern).Should().BeTrue();
        Regex.IsMatch("/v1x0/github", pattern).Should().BeFalse();
    }
}
