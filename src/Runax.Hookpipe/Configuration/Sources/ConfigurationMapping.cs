namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Key-value entries, in source order.
/// </summary>
/// <param name="Path">Where the mapping sits.</param>
/// <param name="Entries">The entries; keys are kept exactly as written.</param>
internal sealed record ConfigurationMapping(ConfigurationNodePath Path, IReadOnlyList<KeyValuePair<string, ConfigurationNode>> Entries)
    : ConfigurationNode(Path);
