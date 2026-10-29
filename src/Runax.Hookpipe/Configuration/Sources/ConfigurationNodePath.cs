namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Where a configuration value sits, for error messages: <c>webhooks.github.validate[0].secret</c> for
/// YAML, or <c>Hookpipe:Webhooks:github:Validate:0:Secret</c> for an <c>IConfiguration</c> section.
/// </summary>
internal sealed class ConfigurationNodePath
{
    private readonly string _text;
    private readonly bool _colonSeparated;

    private ConfigurationNodePath(string text, bool colonSeparated)
    {
        _text = text;
        _colonSeparated = colonSeparated;
    }

    /// <summary>
    /// The root of a YAML document, written with dots and brackets.
    /// </summary>
    public static ConfigurationNodePath YamlRoot { get; } = new(string.Empty, colonSeparated: false);

    /// <summary>
    /// The path of a mapping entry under this path.
    /// </summary>
    public ConfigurationNodePath Child(string key)
    {
        if (_text.Length == 0) return new ConfigurationNodePath(key, _colonSeparated);

        return new ConfigurationNodePath(_colonSeparated ? $"{_text}:{key}" : $"{_text}.{key}", _colonSeparated);
    }

    /// <summary>
    /// The path of a list item under this path.
    /// </summary>
    public ConfigurationNodePath Item(int index) =>
        new(_colonSeparated ? $"{_text}:{index}" : $"{_text}[{index}]", _colonSeparated);

    /// <inheritdoc />
    public override string ToString() => _text.Length == 0 ? "(root)" : _text;
}
