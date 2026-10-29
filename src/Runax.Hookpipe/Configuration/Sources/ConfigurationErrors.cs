namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Collects load errors so one load reports every problem in the file, not just the first.
/// </summary>
internal sealed class ConfigurationErrors
{
    private readonly List<string> _messages = [];

    /// <summary>
    /// The errors so far, each prefixed with its configuration path.
    /// </summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <summary>
    /// Whether any error was added.
    /// </summary>
    public bool HasErrors => _messages.Count > 0;

    /// <summary>
    /// Adds an error at a configuration path.
    /// </summary>
    public void Add(ConfigurationNodePath path, string message) => _messages.Add($"{path}: {message}");

    /// <summary>
    /// Throws a <see cref="HookpipeConfigurationException"/> listing every error, if there are any.
    /// </summary>
    /// <param name="source">The file or section the configuration came from.</param>
    public void ThrowIfAny(string source)
    {
        if (HasErrors) throw new HookpipeConfigurationException(source, _messages);
    }
}
