using System.Text;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Resolves <c>${…}</c> in configuration values when the configuration loads:
/// <list type="bullet">
///   <item><c>${VAR}</c> is the environment variable <c>VAR</c>; a missing one is a load error.</item>
///   <item><c>${VAR:-default}</c> uses <c>default</c> when <c>VAR</c> is unset or empty.</item>
///   <item><c>${file:/run/secrets/name}</c> reads a file, without its trailing newline.</item>
///   <item><c>$${</c> is a literal <c>${</c>.</item>
/// </list>
/// Single-brace <c>{…}</c> templates are left alone; they're filled in per message.
/// </summary>
internal static class ConfigurationInterpolator
{
    private const string FilePrefix = "file:";
    private const string DefaultSeparator = ":-";

    /// <summary>
    /// Interpolates one value. Errors name the variable or file, never a value.
    /// </summary>
    /// <param name="raw">The value as written.</param>
    /// <param name="path">Where the value sits, for errors.</param>
    /// <param name="errors">Collects errors.</param>
    /// <param name="interpolated">Whether anything was filled in, which makes the value a secret.</param>
    /// <returns>The interpolated value, or <paramref name="raw"/> when it has an error.</returns>
    public static string Interpolate(string raw, ConfigurationNodePath path, ConfigurationErrors errors, out bool interpolated)
    {
        interpolated = false;
        if (!raw.Contains('$')) return raw;

        var result = new StringBuilder(raw.Length);
        var index = 0;

        while (index < raw.Length)
        {
            if (raw.AsSpan(index).StartsWith("$${"))
            {
                result.Append("${");
                index += 3;
                continue;
            }

            if (!raw.AsSpan(index).StartsWith("${"))
            {
                result.Append(raw[index]);
                index++;
                continue;
            }

            var end = raw.IndexOf('}', index + 2);
            if (end < 0)
            {
                errors.Add(path, "'${' has no closing '}' (write '$${' for a literal '${')");
                return raw;
            }

            var expression = raw[(index + 2)..end];
            var value = Resolve(expression, path, errors);
            if (value is null) return raw;

            result.Append(value);
            interpolated = true;
            index = end + 1;
        }

        return result.ToString();
    }

    private static string? Resolve(string expression, ConfigurationNodePath path, ConfigurationErrors errors)
    {
        if (expression.StartsWith(FilePrefix, StringComparison.Ordinal))
            return ReadFile(expression[FilePrefix.Length..], path, errors);

        var separator = expression.IndexOf(DefaultSeparator, StringComparison.Ordinal);
        var name = separator < 0 ? expression : expression[..separator];

        if (name.Length == 0)
        {
            errors.Add(path, "'${}' names no environment variable");
            return null;
        }

        var value = Environment.GetEnvironmentVariable(name);
        if (separator >= 0)
            return string.IsNullOrEmpty(value) ? expression[(separator + DefaultSeparator.Length)..] : value;

        if (value is not null) return value;

        errors.Add(path, $"environment variable '{name}' is not set");
        return null;
    }

    private static string? ReadFile(string filePath, ConfigurationNodePath path, ConfigurationErrors errors)
    {
        try
        {
            return File.ReadAllText(filePath).TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add(path, $"can't read secret file '{filePath}': {ex.GetType().Name}");
            return null;
        }
    }
}
