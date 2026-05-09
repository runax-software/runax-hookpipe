using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Hookpipe.Core.Config;
using Hookpipe.Core.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Hookpipe.Core.Tests.Validation;

public sealed class HmacSha256ValidatorTests : IDisposable
{
    private const string EnvVar = "TEST_HMAC_SECRET";
    private const string Secret = "test-secret-key";
    private readonly HmacSha256Validator _validator = new(Substitute.For<ILogger<HmacSha256Validator>>());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    private static ValidationConfig MakeConfig(string header = "X-Signature") => new()
    {
        Signature = new SignatureValidation
        {
            Header = header,
            SecretEnv = EnvVar,
            Algorithm = "hmac-sha256"
        }
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
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"event":"push"}""";
        var signature = ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidSignatureWithPrefix_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"event":"push"}""";
        var signature = "sha256=" + ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongSignature_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string body = """{"event":"push"}""";
        var context = MakeContext(body, "X-Signature", "sha256=invalidhex");

        var result = await _validator.ValidateAsync(context, MakeConfig());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TamperedBody_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable(EnvVar, Secret);
        const string originalBody = """{"event":"push"}""";
        var signature = ComputeSignature(originalBody, Secret);
        var context = MakeContext("""{"event":"tampered"}""", "X-Signature", signature);

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
    public async Task ValidateAsync_EnvVarNotSet_ReturnsFalse()
    {
        const string body = """{"event":"push"}""";
        var context = MakeContext(body, "X-Signature", "somesig");

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
        const string body = """{"event":"push"}""";
        var signature = ComputeSignature(body, Secret);
        var context = MakeContext(body, "X-Signature", signature);

        await _validator.ValidateAsync(context, MakeConfig());

        context.Request.Body.Position.Should().Be(0);
    }
}
