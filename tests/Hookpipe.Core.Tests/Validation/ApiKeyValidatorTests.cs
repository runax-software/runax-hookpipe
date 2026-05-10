using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Validation;

public sealed class ApiKeyValidatorTests : IDisposable
{
    private const string EnvVar = "TEST_API_KEY";
    private readonly ApiKeyValidator _validator = new(Substitute.For<ILogger<ApiKeyValidator>>());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    private static ValidationConfig MakeConfig(string header = "X-API-Key") => new()
    {
        Auth = new AuthValidation { Type = "api-key", TokenEnv = EnvVar, Header = header }
    };

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret-key");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "my-secret-key";

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongKey_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret-key");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret-key");
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_EnvVarNotSet_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "my-secret-key";

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullAuthConfig_ReturnsFalse()
    {
        var config = new ValidationConfig { Auth = null };
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, config);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NoHeaderConfigured_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, "my-secret-key");
        var config = new ValidationConfig
        {
            Auth = new AuthValidation { Type = "api-key", TokenEnv = EnvVar, Header = null }
        };
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "my-secret-key";

        var result = await _validator.ValidateAsync(context, config);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CustomHeaderName_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, "secret-123");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Custom-Auth"] = "secret-123";

        var result = await _validator.ValidateAsync(context, MakeConfig("X-Custom-Auth"));

        result.Should().BeTrue();
    }
}
