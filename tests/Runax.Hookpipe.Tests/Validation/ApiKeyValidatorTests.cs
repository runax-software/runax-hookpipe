using FluentAssertions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Validation;

public sealed class ApiKeyValidatorTests
{
    private const string Key = "my-secret-key";
    private readonly ApiKeyValidator _validator = new(Substitute.For<ILogger<ApiKeyValidator>>());

    private static ValidatorConfiguration MakeConfiguration(string? header = "X-API-Key", string? key = Key) => new()
    {
        Type = ApiKeyValidator.TypeName,
        Header = header,
        Key = key,
    };

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = Key;

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongKey_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_KeyNotConfigured_ReturnsFalse(string? key)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = Key;

        var result = await _validator.ValidateAsync(context, MakeConfiguration(key: key));

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_NoHeaderConfigured_ReturnsFalse(string? header)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = Key;

        var result = await _validator.ValidateAsync(context, MakeConfiguration(header: header));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CustomHeaderName_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Custom-Auth"] = "secret-123";

        var result = await _validator.ValidateAsync(context, MakeConfiguration("X-Custom-Auth", "secret-123"));

        result.Should().BeTrue();
    }
}
