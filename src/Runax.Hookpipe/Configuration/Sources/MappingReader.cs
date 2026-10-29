namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Reads the keys of one mapping and reports the ones nobody asked for. Keys match ignoring case
/// and underscores, so <c>max_retries</c>, <c>MaxRetries</c>, and <c>maxRetries</c> are the same key.
/// </summary>
internal sealed class MappingReader
{
    private readonly Dictionary<string, KeyValuePair<string, ConfigurationNode>> _entries = [];
    private readonly HashSet<string> _read = [];
    private readonly ConfigurationErrors _errors;

    public MappingReader(ConfigurationMapping mapping, ConfigurationErrors errors)
    {
        Path = mapping.Path;
        _errors = errors;

        foreach (var entry in mapping.Entries)
        {
            if (!_entries.TryAdd(Normalize(entry.Key), entry))
                errors.Add(mapping.Path.Child(entry.Key),
                    $"duplicate key (same as '{_entries[Normalize(entry.Key)].Key}')");
        }
    }

    /// <summary>
    /// Where the mapping sits.
    /// </summary>
    public ConfigurationNodePath Path { get; }

    /// <summary>
    /// Returns a key's node, or null when the key is missing or empty, and marks the key as known.
    /// </summary>
    /// <param name="key">The key in snake_case, as documented.</param>
    public ConfigurationNode? Get(string key)
    {
        var normalized = Normalize(key);
        _read.Add(normalized);

        return _entries.TryGetValue(normalized, out var entry) && entry.Value is not ConfigurationScalar { Value: null }
            ? entry.Value
            : null;
    }

    /// <summary>
    /// Whether a key is present with a value.
    /// </summary>
    public bool Has(string key) =>
        _entries.TryGetValue(Normalize(key), out var entry) && entry.Value is not ConfigurationScalar { Value: null };

    /// <summary>
    /// The path a key has, or would have, in this mapping, using the spelling written in the configuration.
    /// </summary>
    public ConfigurationNodePath PathOf(string key) =>
        _entries.TryGetValue(Normalize(key), out var entry) ? Path.Child(entry.Key) : Path.Child(key);

    /// <summary>
    /// Adds an "unknown key" error for every key that was never read.
    /// </summary>
    public void ReportUnknownKeys()
    {
        foreach (var (normalized, entry) in _entries)
            if (!_read.Contains(normalized))
                _errors.Add(Path.Child(entry.Key), "unknown key");
    }

    /// <summary>
    /// Normalizes a property name for matching: no underscores, lower case.
    /// </summary>
    public static string Normalize(string key) => key.Replace("_", string.Empty).ToLowerInvariant();
}
