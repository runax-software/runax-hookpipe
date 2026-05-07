using FluentAssertions;
using Hookpipe.Core.Config;

namespace Hookpipe.Core.Tests.Config;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Load_ValidConfig_ParsesEndpointsAndSinks()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: stdout
                message:
                  include_body: true
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints.Should().HaveCount(1);
        config.Sinks.Should().HaveCount(1);
        config.Endpoints[0].Id.Should().Be("test");
        config.Endpoints[0].Path.Should().Be("/test");
        config.Endpoints[0].Methods.Should().Contain("POST");
        config.Endpoints[0].Sink.Should().Be("stdout");
        config.Sinks[0].Id.Should().Be("stdout");
        config.Sinks[0].Type.Should().Be("stdout");
    }

    [Fact]
    public void Load_MultipleMethods_ParsesAll()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                  - PUT
                sink: stdout
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints[0].Methods.Should().BeEquivalentTo(["POST", "PUT"]);
    }

    [Fact]
    public void Load_WithValidation_ParsesSignatureConfig()
    {
        const string yaml = """
            endpoints:
              - id: github
                path: /github
                methods:
                  - POST
                sink: stdout
                validation:
                  signature:
                    header: X-Hub-Signature-256
                    secret_env: GITHUB_SECRET
                    algorithm: hmac-sha256
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints[0].Validation.Should().NotBeNull();
        config.Endpoints[0].Validation!.Signature.Should().NotBeNull();
        config.Endpoints[0].Validation!.Signature!.Header.Should().Be("X-Hub-Signature-256");
        config.Endpoints[0].Validation!.Signature!.SecretEnv.Should().Be("GITHUB_SECRET");
        config.Endpoints[0].Validation!.Signature!.Algorithm.Should().Be("hmac-sha256");
    }

    [Fact]
    public void Load_WithAuthValidation_ParsesBearerConfig()
    {
        const string yaml = """
            endpoints:
              - id: coolify
                path: /coolify
                methods:
                  - POST
                sink: stdout
                validation:
                  auth:
                    type: bearer
                    token_env: MY_TOKEN
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints[0].Validation!.Auth.Should().NotBeNull();
        config.Endpoints[0].Validation!.Auth!.Type.Should().Be("bearer");
        config.Endpoints[0].Validation!.Auth!.TokenEnv.Should().Be("MY_TOKEN");
    }

    [Fact]
    public void Load_WithMessageConfig_ParsesHeaderFilterAndMetadata()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: stdout
                message:
                  include_body: true
                  include_headers: true
                  header_filter:
                    - X-Custom
                  metadata:
                    source: static-value
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        var msg = config.Endpoints[0].Message;
        msg.IncludeBody.Should().BeTrue();
        msg.IncludeHeaders.Should().BeTrue();
        msg.HeaderFilter.Should().Contain("X-Custom");
        msg.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("static-value");
    }

    [Fact]
    public void Load_NoEndpoints_Throws()
    {
        const string yaml = """
            endpoints: []
            sinks:
              - id: stdout
                type: stdout
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one endpoint*");
    }

    [Fact]
    public void Load_NoSinks_Throws()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: stdout
            sinks: []
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one sink*");
    }

    [Fact]
    public void Load_UnknownSinkReference_Throws()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: nonexistent
            sinks:
              - id: stdout
                type: stdout
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown sink*");
    }

    [Fact]
    public void Load_DuplicateEndpointId_Throws()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test1
                methods:
                  - POST
                sink: stdout
              - id: test
                path: /test2
                methods:
                  - POST
                sink: stdout
            sinks:
              - id: stdout
                type: stdout
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate endpoint*");
    }

    [Fact]
    public void Load_DuplicateSinkId_Throws()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: stdout
            sinks:
              - id: stdout
                type: stdout
              - id: stdout
                type: stdout
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate sink*");
    }

    [Fact]
    public void Load_BothSignatureAndAuth_Throws()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                methods:
                  - POST
                sink: stdout
                validation:
                  signature:
                    header: X-Sig
                    secret_env: SECRET
                    algorithm: hmac-sha256
                  auth:
                    type: bearer
                    token_env: TOKEN
            sinks:
              - id: stdout
                type: stdout
            """;

        var act = () => ConfigLoader.LoadFromString(yaml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both*");
    }

    [Fact]
    public void Load_DefaultMethodIsPost()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                sink: stdout
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints[0].Methods.Should().BeEquivalentTo(["POST"]);
    }

    [Fact]
    public void Load_DefaultMessageIncludesBody()
    {
        const string yaml = """
            endpoints:
              - id: test
                path: /test
                sink: stdout
            sinks:
              - id: stdout
                type: stdout
            """;

        var config = ConfigLoader.LoadFromString(yaml);

        config.Endpoints[0].Message.IncludeBody.Should().BeTrue();
        config.Endpoints[0].Message.IncludeHeaders.Should().BeFalse();
    }
}
