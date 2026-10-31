using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Validation;

public sealed class BearerTokenValidatorTests
{
    private const string Token = "my-secret";
    private readonly BearerTokenValidator _validator = new(Substitute.For<ILogger<BearerTokenValidator>>());

    private static ValidatorConfiguration MakeConfiguration(string? token = Token) => new()
    {
        Type = BearerTokenValidator.TypeName,
        Token = token,
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
        var context = MakeContext("Bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_RawToken_ReturnsTrue()
    {
        var context = MakeContext("my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongToken_ReturnsFalse()
    {
        var context = MakeContext("Bearer wrong-token");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        var context = MakeContext(null);

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_TokenNotConfigured_ReturnsFalse(string? token)
    {
        var context = MakeContext("Bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfiguration(token));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_CaseInsensitiveBearerPrefix_ReturnsTrue()
    {
        var context = MakeContext("bearer my-secret");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeTrue();
    }
}
