using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Binds a broker's <c>settings</c> mapping onto its settings class: <c>exchange_name</c> sets
/// <c>ExchangeName</c>. Dictionary keys, such as topic names, are kept exactly as written.
/// Properties marked <see cref="RequiredAttribute"/> must be set and not empty.
/// </summary>
internal static class SettingsBinder
{
    /// <summary>
    /// Creates the settings object and binds the mapping onto it.
    /// </summary>
    /// <param name="settingsType">The settings class; it needs a public parameterless constructor.</param>
    /// <param name="node">The <c>settings</c> node, or null when the configuration has none.</param>
    /// <param name="path">Where the settings sit, for errors about missing keys.</param>
    /// <param name="reader">Reads values and collects errors.</param>
    public static object Bind(Type settingsType, ConfigurationNode? node, ConfigurationNodePath path, ScalarReader reader)
    {
        var settings = Activator.CreateInstance(settingsType)
                       ?? throw new InvalidOperationException($"Can't create '{settingsType.Name}'");
        var properties = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .ToDictionary(property => MappingReader.Normalize(property.Name));

        if (node is not null && reader.Mapping(node) is { } mapping)
        {
            foreach (var (normalized, property) in properties)
                if (mapping.Get(normalized) is { } valueNode && Read(valueNode, property.PropertyType, reader) is { } value)
                    property.SetValue(settings, value);

            mapping.ReportUnknownKeys();
        }

        foreach (var property in properties.Values)
        {
            if (property.GetCustomAttribute<RequiredAttribute>() is not null && IsEmpty(property.GetValue(settings)))
                reader.Errors.Add(path.Child(ToSnakeCase(property.Name)), "required");
        }

        return settings;
    }

    private static object? Read(ConfigurationNode node, Type type, ScalarReader reader)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(string)) return reader.String(node);
        if (target == typeof(int)) return reader.Int(node, minimum: 0);
        if (target == typeof(bool)) return reader.Bool(node);
        if (target == typeof(double)) return reader.Double(node, minimum: 0);
        if (target == typeof(TimeSpan)) return reader.Duration(node);
        if (target == typeof(Dictionary<string, string>)) return ReadDictionary(node, reader);

        throw new InvalidOperationException($"Settings property type '{type.Name}' isn't supported");
    }

    private static Dictionary<string, string>? ReadDictionary(ConfigurationNode node, ScalarReader reader)
    {
        if (node is not ConfigurationMapping mapping)
        {
            reader.Errors.Add(node.Path, "expected a mapping of keys and values");
            return null;
        }

        var dictionary = new Dictionary<string, string>();
        foreach (var (key, valueNode) in mapping.Entries)
            if (reader.String(valueNode) is { } value)
                dictionary[key] = value;

        return dictionary;
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        System.Collections.ICollection collection => collection.Count == 0,
        _ => false,
    };

    /// <summary>
    /// Converts a property name to the snake_case key it's documented as: <c>ExchangeName</c>
    /// becomes <c>exchange_name</c>.
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
