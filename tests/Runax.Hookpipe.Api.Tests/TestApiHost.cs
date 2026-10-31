using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Runax.Hookpipe.Api.Tests;

/// <summary>
/// Runs the service on a temporary configuration file, with optional environment variables, and restores
/// the environment when disposed.
/// </summary>
internal sealed class TestApiHost : IDisposable
{
    private readonly string _configurationPath;
    private readonly Dictionary<string, string?> _previousEnv = [];

    public TestApiHost(string yaml, Dictionary<string, string?>? env = null)
    {
        _configurationPath = Path.Combine(Path.GetTempPath(), $"hookpipe-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(_configurationPath, yaml);

        SetEnvironmentVariable("HOOKPIPE_CONFIG_PATH", _configurationPath);
        foreach (var (key, value) in env ?? [])
            SetEnvironmentVariable(key, value);

        Factory = new WebApplicationFactory<Program>();
    }

    public WebApplicationFactory<Program> Factory { get; }

    public void Dispose()
    {
        Factory.Dispose();

        foreach (var (key, value) in _previousEnv)
            Environment.SetEnvironmentVariable(key, value);

        if (File.Exists(_configurationPath))
            File.Delete(_configurationPath);
    }

    /// <summary>
    /// Asserts that <paramref name="metrics"/> has a <paramref name="metricName"/> sample with every
    /// label given, in any order, and the value <paramref name="value"/>.
    /// </summary>
    public static void AssertMetric(
        string metrics,
        string metricName,
        int value,
        params (string Name, string Value)[] labels)
    {
        var lines = metrics.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.ShouldContain(line =>
            line.StartsWith($"{metricName}{{", StringComparison.Ordinal) &&
            labels.All(label => line.Contains($"{label.Name}=\"{label.Value}\"", StringComparison.Ordinal)) &&
            line.EndsWith($" {value}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts that no <paramref name="metricName"/> sample has every label given.
    /// </summary>
    public static void AssertNoMetric(string metrics, string metricName, params (string Name, string Value)[] labels)
    {
        var lines = metrics.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.ShouldNotContain(line =>
            line.StartsWith($"{metricName}{{", StringComparison.Ordinal) &&
            labels.All(label => line.Contains($"{label.Name}=\"{label.Value}\"", StringComparison.Ordinal)));
    }

    private void SetEnvironmentVariable(string key, string? value)
    {
        _previousEnv.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
}
