using System.Text.Json;
using System.Text.Json.Serialization;
using Hookpipe.Core.Config;

namespace Hookpipe.Core.Sinks;

/// <summary>
/// Shared utilities for sink implementations.
/// </summary>
internal static class SinkHelper
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Loads a required env var from sink settings.
    /// </summary>
    internal static string RequireEnvVar(SinkConfig config, string settingKey, string defaultEnvVar)
    {
        var envVar = config.Settings.GetValueOrDefault(settingKey, defaultEnvVar);

        return Environment.GetEnvironmentVariable(envVar)
               ?? throw new InvalidOperationException($"Sink '{config.Id}': env var '{envVar}' is not set");
    }

    /// <summary>
    /// Loads an optional env var from sink settings.
    /// </summary>
    internal static string? OptionalEnvVar(SinkConfig config, string settingKey, string defaultEnvVar) =>
        Environment.GetEnvironmentVariable(config.Settings.GetValueOrDefault(settingKey, defaultEnvVar));
}
