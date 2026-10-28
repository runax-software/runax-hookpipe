using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Runax.Hookpipe.Hosting;

namespace Runax.Hookpipe.Tests.Hosting;

public sealed class HookpipeHostingTests : IDisposable
{
    private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");

    [Fact]
    public async Task AddHookpipe_MapHookpipe_ServesWebhookWithoutTheService()
    {
        File.WriteAllText(_configPath, """
            endpoints:
              - id: library-host
                path: /library-host
                methods:
                  - POST
                sink: stdout-dev
            sinks:
              - id: stdout-dev
                type: stdout
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHookpipe(hookpipe => hookpipe.LoadYaml(_configPath, reloadOnChange: false));

        await using var app = builder.Build();
        app.MapHookpipe();
        app.MapHookpipeHealth();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/library-host", new StringContent("""{"hello":"library"}""", Encoding.UTF8, "application/json"));
        var health = await client.GetAsync("/health/sinks");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).Should().Contain("\"stdout-dev\"");
    }

    [Fact]
    public void AddHookpipe_WithoutConfig_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHookpipe(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*LoadYaml*");
    }

    [Fact]
    public void AddHookpipe_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddHookpipe(hookpipe => hookpipe.LoadYaml(_configPath));

        var act = () => services.AddHookpipe(hookpipe => hookpipe.LoadYaml(_configPath));

        act.Should().Throw<InvalidOperationException>().WithMessage("*only once*");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }
}
