using Runax.Hookpipe.Configuration;
using Shouldly;

namespace Runax.Hookpipe.Tests.Configuration;

/// <summary>
/// Covers the byte-size scalar behind <c>server.max_body_size</c> and
/// <c>webhooks.&lt;id&gt;.max_body_size</c>.
/// </summary>
public sealed class ByteSizeTests
{
    [Theory]
    [InlineData("512B", 512L)]
    [InlineData("1B", 1L)]
    [InlineData("64KB", 64L * 1024)]
    [InlineData("25MB", 25L * 1024 * 1024)]
    [InlineData("1GB", 1024L * 1024 * 1024)]
    [InlineData("8GB", 8L * 1024 * 1024 * 1024)]
    [InlineData("2.5MB", 2621440L)]
    [InlineData("10mb", 10L * 1024 * 1024)]
    [InlineData("8Kb", 8L * 1024)]
    public void MaxBodySize_ValidSize_IsReadInBytes(string size, long expected)
    {
        var configuration = Load($"max_body_size: {size}");

        configuration.Server.MaxBodySize.ShouldBe(expected);
        configuration.Webhooks["github"].MaxBodySize.ShouldBe(expected);
    }

    [Fact]
    public void MaxBodySize_Absent_IsNullSoTheHostKeepsItsDefault()
    {
        var configuration = ConfigurationLoader.LoadFromString("""
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              github:
                to: console
            """);

        configuration.Server.MaxBodySize.ShouldBeNull();
        configuration.Webhooks["github"].MaxBodySize.ShouldBeNull();
    }

    [Theory]
    [InlineData("25")]
    [InlineData("25 MB")]
    [InlineData("MB")]
    [InlineData("25TB")]
    [InlineData("-1MB")]
    [InlineData("big")]
    [InlineData("25MB extra")]
    public void MaxBodySize_NotASize_FailsWithAnExample(string size)
    {
        var act = () => Load($"max_body_size: {size}");

        var exception = Should.Throw<HookpipeConfigurationException>(act);
        exception.Errors.ShouldAllBe(error =>
            error.Contains($"expected a size such as 512B, 64KB, 25MB, or 1GB, got '{size}'"));
    }

    [Theory]
    [InlineData("0B", "must be at least 1 byte")]
    [InlineData("0.4B", "must be at least 1 byte")]
    [InlineData("99999999999GB", "is larger than Hookpipe can represent")]
    public void MaxBodySize_OutOfRange_FailsWithTheReason(string size, string reason)
    {
        var act = () => Load($"max_body_size: {size}");

        var exception = Should.Throw<HookpipeConfigurationException>(act);
        exception.Errors.ShouldAllBe(error => error.Contains(reason));
    }

    /// <summary>
    /// A size at 2^63 has to be reported, not silently clamped to <c>long.MaxValue</c> by the cast.
    /// Both of these parse to exactly 2^63 as a <c>double</c>, which is also where
    /// <c>long.MaxValue</c> rounds to once widened, so the guard can't compare against it directly.
    /// </summary>
    [Theory]
    [InlineData("9223372036854775807B")]
    [InlineData("9223372036854775808B")]
    public void MaxBodySize_AtTheLongBoundary_IsTooLargeToRepresent(string size)
    {
        var act = () => Load($"max_body_size: {size}");

        var exception = Should.Throw<HookpipeConfigurationException>(act);
        exception.Errors.ShouldAllBe(error => error.Contains("is larger than Hookpipe can represent"));
    }

    [Fact]
    public void MaxBodySize_NotASize_NamesTheConfigurationPath()
    {
        var act = () => Load("max_body_size: huge");

        var exception = Should.Throw<HookpipeConfigurationException>(act);
        exception.Errors.ShouldBe(
            [
                "server.max_body_size: expected a size such as 512B, 64KB, 25MB, or 1GB, got 'huge'",
                "webhooks.github.max_body_size: expected a size such as 512B, 64KB, 25MB, or 1GB, got 'huge'",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void MaxBodySize_FromAnInterpolatedValue_NeverEchoesIt()
    {
        var variable = $"HOOKPIPE_TEST_SIZE_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "not-a-size");

        try
        {
            var act = () => Load($"max_body_size: ${{{variable}}}");

            var exception = Should.Throw<HookpipeConfigurationException>(act);
            exception.Errors.ShouldAllBe(error => error.Contains("got the interpolated value"));
            exception.Errors.ShouldAllBe(error => !error.Contains("not-a-size"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void MaxBodySize_NotAScalar_FailsWithAnExample()
    {
        var act = () => ConfigurationLoader.LoadFromString("""
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              github:
                max_body_size: [25MB]
                to: console
            """);

        var exception = Should.Throw<HookpipeConfigurationException>(act);
        exception.Errors.ShouldBe(
            ["webhooks.github.max_body_size: expected a size such as 512B, 64KB, 25MB, or 1GB"]);
    }

    /// <summary>
    /// Loads a configuration with the same <c>max_body_size</c> line on the server and on a webhook.
    /// </summary>
    private static HookpipeConfiguration Load(string line) => ConfigurationLoader.LoadFromString($"""
        version: 2
        server:
          {line}
        brokers:
          console:
            type: stdout
        webhooks:
          github:
            {line}
            to: console
        """);
}
