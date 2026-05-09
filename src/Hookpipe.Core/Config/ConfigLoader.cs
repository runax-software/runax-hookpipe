using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hookpipe.Core.Config;

/// <summary>
/// Loads and validates Hookpipe configuration from YAML.
/// </summary>
public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly HashSet<string> KnownValidatorTypes = ["bearer", "hmac-sha256"];

    /// <summary>
    /// Loads configuration from the specified YAML file path.
    /// </summary>
    /// <param name="path">Absolute or relative path to the YAML config file.</param>
    /// <returns>Parsed and validated <see cref="HookpipeConfig"/>.</returns>
    /// <exception cref="FileNotFoundException">Config file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Config is empty or invalid.</exception>
    public static HookpipeConfig Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Config file not found", path);

        var yaml = File.ReadAllText(path);

        return LoadFromString(yaml);
    }

    /// <summary>
    /// Loads configuration from a YAML string.
    /// </summary>
    /// <param name="yaml">YAML content to parse.</param>
    /// <returns>Parsed and validated <see cref="HookpipeConfig"/>.</returns>
    /// <exception cref="InvalidOperationException">Config is empty or invalid.</exception>
    public static HookpipeConfig LoadFromString(string yaml)
    {
        var config = Deserializer.Deserialize<HookpipeConfig>(yaml)
                     ?? throw new InvalidOperationException("Config file is empty");

        Validate(config);
        return config;
    }

    /// <summary>
    /// Validates that the config has required fields, no duplicates, and all sink references resolve.
    /// </summary>
    private static void Validate(HookpipeConfig config)
    {
        Ensure(config.Endpoints.Count > 0, "Config must define at least one endpoint");
        Ensure(config.Sinks.Count > 0, "Config must define at least one sink");

        var sinkIds = config.Sinks.Select(sink => sink.Id).ToHashSet();
        var endpointIds = new HashSet<string>();

        foreach (var endpoint in config.Endpoints)
        {
            EnsureNotEmpty(endpoint.Id, "Endpoint is missing an 'id'");
            Ensure(endpointIds.Add(endpoint.Id), $"Duplicate endpoint id: '{endpoint.Id}'");
            EnsureNotEmpty(endpoint.Path, $"Endpoint '{endpoint.Id}' is missing a 'path'");
            Ensure(endpoint.Methods.Count > 0, $"Endpoint '{endpoint.Id}' must have at least one method");
            EnsureNotEmpty(endpoint.Sink, $"Endpoint '{endpoint.Id}' is missing a 'sink'");
            Ensure(sinkIds.Contains(endpoint.Sink),
                $"Endpoint '{endpoint.Id}' references unknown sink '{endpoint.Sink}'");

            ValidateValidation(endpoint);
        }

        var sinkIdSet = new HashSet<string>();
        foreach (var sink in config.Sinks)
        {
            EnsureNotEmpty(sink.Id, "Sink is missing an 'id'");
            Ensure(sinkIdSet.Add(sink.Id), $"Duplicate sink id: '{sink.Id}'");
            EnsureNotEmpty(sink.Type, $"Sink '{sink.Id}' is missing a 'type'");
        }
    }

    /// <summary>
    /// Validates the validation block of an endpoint — ensures only one method is set
    /// and all required fields are present.
    /// </summary>
    private static void ValidateValidation(EndpointConfig endpoint)
    {
        var validation = endpoint.Validation;
        if (validation is null) return;

        Ensure(validation.Signature is null || validation.Auth is null,
            $"Endpoint '{endpoint.Id}' has both 'signature' and 'auth' validation — pick one");

        if (validation.Signature is not null)
        {
            EnsureNotEmpty(validation.Signature.Header,
                $"Endpoint '{endpoint.Id}' signature validation is missing 'header'");
            EnsureNotEmpty(validation.Signature.SecretEnv,
                $"Endpoint '{endpoint.Id}' signature validation is missing 'secret_env'");
            EnsureNotEmpty(validation.Signature.Algorithm,
                $"Endpoint '{endpoint.Id}' signature validation is missing 'algorithm'");
            Ensure(KnownValidatorTypes.Contains(validation.Signature.Algorithm),
                $"Endpoint '{endpoint.Id}' references unknown validator type '{validation.Signature.Algorithm}'");
        }

        if (validation.Auth is null) return;

        EnsureNotEmpty(validation.Auth.Type, $"Endpoint '{endpoint.Id}' auth validation is missing 'type'");
        EnsureNotEmpty(validation.Auth.TokenEnv, $"Endpoint '{endpoint.Id}' auth validation is missing 'token_env'");
        Ensure(KnownValidatorTypes.Contains(validation.Auth.Type),
            $"Endpoint '{endpoint.Id}' references unknown auth type '{validation.Auth.Type}'");
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the condition is false.
    /// </summary>
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the value is null or whitespace.
    /// </summary>
    private static void EnsureNotEmpty(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
    }
}
