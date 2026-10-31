using System.Security.Cryptography;
using System.Text;
using Runax.Hookpipe.Configuration;
using Runax.Hookpipe.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Runax.Hookpipe.Tests.Validation;

public sealed class StripeSignatureValidatorTests
{
    private const string Secret = "whsec_test_secret";
    private readonly StripeSignatureValidator _validator = new(Substitute.For<ILogger<StripeSignatureValidator>>());

    private static ValidatorConfiguration MakeConfiguration(string? header = null, string? secret = Secret) => new()
    {
        Type = StripeSignatureValidator.TypeName,
        Header = header,
        Secret = secret,
    };

    private static string ComputeStripeSignature(string body, string secret, long timestamp)
    {
        var signedPayload = $"{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
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
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CustomHeader_ReturnsTrue()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "X-Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("X-Stripe-Signature"));

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CustomHeaderButDefaultHeaderSent_ReturnsFalse()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration("X-Stripe-Signature"));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WrongSignature_ReturnsFalse()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1=invalidsignature");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredTimestamp_ReturnsFalse()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TamperedBody_ReturnsFalse()
    {
        const string originalBody = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(originalBody, Secret, timestamp);
        var context = MakeContext("""{"type":"tampered"}""", "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("body"));

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MalformedHeader_ReturnsFalse()
    {
        var context = MakeContext("body", "Stripe-Signature", "not-a-valid-header");

        var result = await _validator.ValidateAsync(context, MakeConfiguration());

        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_SecretNotConfigured_ReturnsFalse(string? secret)
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfiguration(secret: secret));

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_BodyPositionResetAfterValidation()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        await _validator.ValidateAsync(context, MakeConfiguration());

        context.Request.Body.Position.ShouldBe(0);
    }
}
