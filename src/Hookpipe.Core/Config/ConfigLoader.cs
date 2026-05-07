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
        if (config.Endpoints.Count == 0)
            throw new InvalidOperationException("Config must define at least one endpoint");

        if (config.Sinks.Count == 0)
            throw new InvalidOperationException("Config must define at least one sink");

        var sinkIds = config.Sinks.Select(s => s.Id).ToHashSet();
        var endpointIds = new HashSet<string>();

        foreach (var endpoint in config.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Id))
                throw new InvalidOperationException("Endpoint is missing an 'id'");

            if (!endpointIds.Add(endpoint.Id))
                throw new InvalidOperationException($"Duplicate endpoint id: '{endpoint.Id}'");

            if (string.IsNullOrWhiteSpace(endpoint.Path))
                throw new InvalidOperationException($"Endpoint '{endpoint.Id}' is missing a 'path'");

            if (endpoint.Methods.Count == 0)
                throw new InvalidOperationException($"Endpoint '{endpoint.Id}' must have at least one method");

            if (string.IsNullOrWhiteSpace(endpoint.Sink))
                throw new InvalidOperationException($"Endpoint '{endpoint.Id}' is missing a 'sink'");

            if (!sinkIds.Contains(endpoint.Sink))
                throw new InvalidOperationException(
                    $"Endpoint '{endpoint.Id}' references unknown sink '{endpoint.Sink}'");

            ValidateValidation(endpoint);
        }

        var sinkIdSet = new HashSet<string>();
        foreach (var sink in config.Sinks)
        {
            if (string.IsNullOrWhiteSpace(sink.Id))
                throw new InvalidOperationException("Sink is missing an 'id'");

            if (!sinkIdSet.Add(sink.Id))
                throw new InvalidOperationException($"Duplicate sink id: '{sink.Id}'");

            if (string.IsNullOrWhiteSpace(sink.Type))
                throw new InvalidOperationException($"Sink '{sink.Id}' is missing a 'type'");
        }
    }

    /// <summary>
    /// Validates the validation block of an endpoint — ensures only one method is set
    /// and all required fields are present.
    /// </summary>
    private static void ValidateValidation(EndpointConfig endpoint)
    {
        var validation = endpoint.Validation;
        if (validation is null)
            return;

        if (validation.Signature is not null && validation.Auth is not null)
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Id}' has both 'signature' and 'auth' validation — pick one");

        if (validation.Signature is not null)
        {
            if (string.IsNullOrWhiteSpace(validation.Signature.Header))
                throw new InvalidOperationException(
                    $"Endpoint '{endpoint.Id}' signature validation is missing 'header'");

            if (string.IsNullOrWhiteSpace(validation.Signature.SecretEnv))
                throw new InvalidOperationException(
                    $"Endpoint '{endpoint.Id}' signature validation is missing 'secret_env'");

            if (string.IsNullOrWhiteSpace(validation.Signature.Algorithm))
                throw new InvalidOperationException(
                    $"Endpoint '{endpoint.Id}' signature validation is missing 'algorithm'");
        }

        if (validation.Auth is null) return;
        if (string.IsNullOrWhiteSpace(validation.Auth.Type))
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Id}' auth validation is missing 'type'");

        if (string.IsNullOrWhiteSpace(validation.Auth.TokenEnv))
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Id}' auth validation is missing 'token_env'");
    }
}