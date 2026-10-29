using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Runax.Hookpipe.Hosting;

namespace Runax.Hookpipe.Tests.Hosting;

public sealed class HookpipeHostingTests : IDisposable
{
    private readonly string _configurationPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");

    [Fact]
    public async Task AddRunaxHookpipe_MapHookpipe_ServesWebhookWithoutTheService()
    {
        File.WriteAllText(_configurationPath, """
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              library-host:
                to: console
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath, reloadOnChange: false));

        await using var app = builder.Build();
        app.MapHookpipe();
        app.MapHookpipeHealth();
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/webhooks/library-host", new StringContent("""{"hello":"library"}""", Encoding.UTF8, "application/json"));
        var health = await client.GetAsync("/health/brokers");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).Should().Contain("\"console\"");
    }

    [Fact]
    public async Task MapHookpipe_PathWithParameter_ServesWebhook()
    {
        File.WriteAllText(_configurationPath, """
            version: 2
            brokers:
              console:
                type: stdout
            webhooks:
              ingest:
                path: ingest/{source}/events
                message:
                  metadata:
                    source: "{source}"
                to: console
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath, reloadOnChange: false));

        await using var app = builder.Build();
        app.MapHookpipe();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync(
            "/webhooks/ingest/github/events", new StringContent("""{"hello":"params"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public void AddRunaxHookpipe_WithoutConfiguration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddRunaxHookpipe(_ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("*LoadYaml*");
    }

    [Fact]
    public void AddRunaxHookpipe_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath));

        var act = () => services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath));

        act.Should().Throw<InvalidOperationException>().WithMessage("*only once*");
    }

    [Fact]
    public void LoadYaml_CalledTwice_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddRunaxHookpipe(hookpipe => hookpipe
            .LoadYaml(_configurationPath)
            .LoadYaml("other.yaml"));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{_configurationPath}'*LoadYaml only once*");
    }

    [Fact]
    public void AddRunaxHookpipe_RegistersRuntimeAndHostedService()
    {
        var services = new ServiceCollection();

        services.AddRunaxHookpipe(hookpipe => hookpipe.LoadYaml(_configurationPath, reloadOnChange: false));

        services.Should().ContainSingle(d => d.ServiceType == typeof(HookpipeRuntime));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService) &&
                                             d.ImplementationType == typeof(HookpipeHostedService));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_configurationPath))
            File.Delete(_configurationPath);
    }
}
