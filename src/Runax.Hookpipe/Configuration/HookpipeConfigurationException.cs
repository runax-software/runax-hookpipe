namespace Runax.Hookpipe.Configuration;

/// <summary>
/// Thrown when a Hookpipe configuration can't be loaded. Lists every problem found, each with the configuration
/// path it's at. Values filled in by <c>${…}</c> interpolation never appear in the message.
/// </summary>
public sealed class HookpipeConfigurationException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception for a configuration source and its errors.
    /// </summary>
    /// <param name="source">The file or section the configuration came from.</param>
    /// <param name="errors">The errors, each prefixed with its configuration path.</param>
    public HookpipeConfigurationException(string source, IReadOnlyList<string> errors)
        : base(FormatMessage(source, errors))
    {
        ConfigurationSource = source;
        Errors = errors;
    }

    /// <summary>
    /// The file or section the configuration came from.
    /// </summary>
    public string ConfigurationSource { get; }

    /// <summary>
    /// Each problem found, prefixed with its configuration path, such as
    /// <c>webhooks.github.validate[0].secrett: unknown key</c>.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    private static string FormatMessage(string source, IReadOnlyList<string> errors) =>
        errors.Count == 1
            ? $"Invalid Hookpipe config '{source}': {errors[0]}"
            : $"Invalid Hookpipe config '{source}' ({errors.Count} errors):{Environment.NewLine}  " +
              string.Join($"{Environment.NewLine}  ", errors);
}
