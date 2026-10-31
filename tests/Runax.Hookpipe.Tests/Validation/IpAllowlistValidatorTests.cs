using System.Net;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Validation;

public sealed class IpAllowlistValidatorTests
{
    private readonly IpAllowlistValidator _validator = new(Substitute.For<ILogger<IpAllowlistValidator>>());

    private static ValidatorConfiguration MakeConfiguration(params string[] allow) => new()
    {
        Type = IpAllowlistValidator.TypeName,
        Allow = allow,
    };

    private static HttpContext MakeContext(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public async Task ValidateAsync_ExactIpMatch_ReturnsTrue()
    {
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("10.0.0.1"));

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_IpNotInList_ReturnsFalse()
    {
        var context = MakeContext("10.0.0.2");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("10.0.0.1"));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CidrMatch_ReturnsTrue()
    {
        var context = MakeContext("192.168.1.100");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("192.168.1.0/24"));

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CidrNoMatch_ReturnsFalse()
    {
        var context = MakeContext("192.168.2.1");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("192.168.1.0/24"));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MultipleEntries_MatchesSecond()
    {
        var context = MakeContext("192.168.1.50");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("10.0.0.1", "192.168.1.0/24"));

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_EmptyAllow_ReturnsFalse()
    {
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullRemoteIp_ReturnsFalse()
    {
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, MakeConfiguration("10.0.0.1"));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_Slash16Cidr_ReturnsTrue()
    {
        var context = MakeContext("140.82.112.1");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("140.82.0.0/16"));

        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/40")]
    public async Task ValidateAsync_PrefixOutOfRange_ReturnsFalse(string cidr)
    {
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, MakeConfiguration(cidr));

        result.ShouldBeFalse();
    }
}
