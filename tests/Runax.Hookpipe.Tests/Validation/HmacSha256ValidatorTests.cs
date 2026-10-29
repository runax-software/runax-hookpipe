using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Runax.Hookpipe.Tests.Validation;

public sealed class HmacSha256ValidatorTests
{
    private const string Secret = "test-secret-key";
    private readonly HmacSha256Validator _validator = new(Substitute.For<ILogger<HmacSha256Validator>>());

    private static ValidatorConfiguration MakeConfiguration(string? header = "X-Signature", string? secret = Secret) => new()
    {
        Type = HmacSha256Validator.TypeName,
        Header = header,
        Secret = secret,
    };

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpContext MakeContext(string body, string headerName, string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = body.Length;
        context.Request.Headers[headerName] = headerValue;
        return context;
    }

    [Fact]
    public async Task ValidateAsync_ValidSignature_ReturnsTrue()
    {
        const string body = """{"event":"push"}""";
        var signature = ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidSignatureWithPrefix_ReturnsTrue()
    {
        const string body = """{"event":"push"}""";
        var signature = "sha256=" + ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongSignature_ReturnsFalse()
    {
        const string body = """{"event":"push"}""";
        var context = MakeContext(body, "X-Signature", "sha256=invalidhex");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TamperedBody_ReturnsFalse()
    {
        const string originalBody = """{"event":"push"}""";
        var signature = ComputeSignature(originalBody, Secret);
        var context = MakeContext("""{"event":"tampered"}""", "X-Signature", signature);

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("body"));

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_SecretNotConfigured_ReturnsFalse(string? secret)
    {
        const string body = """{"event":"push"}""";
        var context = MakeContext(body, "X-Signature", ComputeSignature(body, Secret));

        var result = await _validator.ValidateAsync(context, MakeConfiguration(secret: secret));

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_HeaderNotConfigured_ReturnsFalse(string? header)
    {
        const string body = """{"event":"push"}""";
        var context = MakeContext(body, "X-Signature", ComputeSignature(body, Secret));

        var result = await _validator.ValidateAsync(context, MakeConfiguration(header: header));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_BodyPositionResetAfterValidation()
    {
        const string body = """{"event":"push"}""";
        var signature = ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        await _validator.ValidateAsync(context, MakeConfiguration());

        context.Request.Body.Position.Should().Be(0);
    }
}
