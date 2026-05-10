using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Validation;

public sealed class StripeSignatureValidatorTests : IDisposable
{
    private const string EnvVar = "TEST_STRIPE_SECRET";
    private const string Secret = "whsec_test_secret";
    private readonly StripeSignatureValidator _validator = new(Substitute.For<ILogger<StripeSignatureValidator>>());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    private static ValidationConfig MakeConfig(string header = "Stripe-Signature") => new()
    {
        Signature = new SignatureValidation
        {
            Header = header,
            SecretEnv = EnvVar,
            Algorithm = "stripe-v1"
        }
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
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongSignature_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1=invalidsignature");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredTimestamp_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TamperedBody_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string originalBody = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(originalBody, Secret, timestamp);
        var context = MakeContext("""{"type":"tampered"}""", "Stripe-Signature", $"t={timestamp},v1={sig}");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingHeader_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("body"));

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MalformedHeader_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        var context = MakeContext("body", "Stripe-Signature", "not-a-valid-header");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_EnvVarNotSet_ReturnsFalse()
    {
        const string body = """{"type":"charge.succeeded"}""";
        var context = MakeContext(body, "Stripe-Signature", "t=123,v1=abc");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullSignatureConfig_ReturnsFalse()
    {
        var config = new ValidationConfig { Signature = null };
        var context = new DefaultHttpContext();

        var result = await _validator.ValidateAsync(context, config);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_BodyPositionResetAfterValidation()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"type":"charge.succeeded"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeStripeSignature(body, Secret, timestamp);
        var context = MakeContext(body, "Stripe-Signature", $"t={timestamp},v1={sig}");

        await _validator.ValidateAsync(context, MakeConfig());

        context.Request.Body.Position.Should().Be(0);
    }
}
