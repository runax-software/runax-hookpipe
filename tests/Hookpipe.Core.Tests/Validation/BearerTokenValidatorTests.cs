using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;

namespace Hookpipe.Core.Tests.Validation;

public sealed class BearerTokenValidatorTests : IDisposable
{
    private const string EnvVar = "TEST_BEARER_TOKEN";
    private readonly BearerTokenValidator _validator = new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    private static ValidationConfig MakeConfig() => new()
    {
        Auth = new AuthValidation { Type = "bearer", TokenEnv = EnvVar }
    };

    private static HttpContext MakeContext(string? authHeader)
    {
        var context = new DefaultHttpContext();
        if (authHeader is not null)
            context.Request.Headers.Authorization = authHeader;
        return context;
    }

    [Fact]
    public async Task ValidateAsync_ValidBearerToken_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret");
        var context = MakeContext("Bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RawToken_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret");
        var context = MakeContext("my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongToken_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret");
        var context = MakeContext("Bearer wrong-token");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret");
        var context = MakeContext(null);

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_EnvVarNotSet_ReturnsFalse()
    {
        var context = MakeContext("Bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullAuthConfig_ReturnsFalse()
    {
        var config = new ValidationConfig { Auth = null };
        var context = MakeContext("Bearer my-secret");

        var result = await _validator.ValidateAsync(context, config);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CaseInsensitiveBearerPrefix_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret");
        var context = MakeContext("bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }
}
