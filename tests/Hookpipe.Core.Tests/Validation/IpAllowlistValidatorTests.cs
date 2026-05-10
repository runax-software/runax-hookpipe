using System.Net;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Validation;

public sealed class IpAllowlistValidatorTests : IDisposable
{
    private const string EnvVar = "TEST_ALLOWED_IPS";
    private readonly IpAllowlistValidator _validator = new(Substitute.For<ILogger<IpAllowlistValidator>>());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    private static ValidationConfig MakeConfig() => new()
    {
        Auth = new AuthValidation { Type = "ip-allowlist", TokenEnv = EnvVar }
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
        Environment.SetEnvironmentVariable(EnvVar, "10.0.0.1");
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_IpNotInList_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "10.0.0.1");
        var context = MakeContext("10.0.0.2");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CidrMatch_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "192.168.1.0/24");
        var context = MakeContext("192.168.1.100");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CidrNoMatch_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "192.168.1.0/24");
        var context = MakeContext("192.168.2.1");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MultipleEntries_MatchesSecond()
    {
        Environment.SetEnvironmentVariable(EnvVar, "10.0.0.1,192.168.1.0/24");
        var context = MakeContext("192.168.1.50");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_EnvVarNotSet_ReturnsFalse()
    {
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullAuthConfig_ReturnsFalse()
    {
        var config = new ValidationConfig { Auth = null };
        var context = MakeContext("10.0.0.1");

        var result = await _validator.ValidateAsync(context, config);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullRemoteIp_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "10.0.0.1");
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_Slash16Cidr_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "140.82.0.0/16");
        var context = MakeContext("140.82.112.1");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }
}
