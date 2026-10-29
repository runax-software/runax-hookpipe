using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// Reads a YAML document into configuration nodes, interpolating <c>${…}</c> in every value.
/// </summary>
internal static class YamlSource
{
    private static readonly HashSet<string> NullScalars = ["", "~", "null", "Null", "NULL"];

    /// <summary>
    /// Parses YAML into a node tree.
    /// </summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="errors">Collects syntax and interpolation errors.</param>
    /// <returns>The root node, or null when the document is empty or isn't valid YAML.</returns>
    public static ConfigurationNode? Read(string yaml, ConfigurationErrors errors)
    {
        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            errors.Add(ConfigurationNodePath.YamlRoot,
                $"invalid YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}");
            return null;
        }

        if (stream.Documents.Count == 0) return null;
        if (stream.Documents.Count > 1)
            errors.Add(ConfigurationNodePath.YamlRoot, "the file holds more than one YAML document");

        return Convert(stream.Documents[0].RootNode, ConfigurationNodePath.YamlRoot, errors);
    }

    private static ConfigurationNode Convert(YamlNode node, ConfigurationNodePath path, ConfigurationErrors errors) => node switch
    {
        YamlMappingNode mapping => new ConfigurationMapping(path, mapping.Children
            .Select(entry =>
            {
                var key = entry.Key is YamlScalarNode scalarKey ? scalarKey.Value ?? string.Empty : entry.Key.ToString();
                return new KeyValuePair<string, ConfigurationNode>(key, Convert(entry.Value, path.Child(key), errors));
            })
            .ToList()),
        YamlSequenceNode sequence => new ConfigurationSequence(path, sequence.Children
            .Select((item, index) => Convert(item, path.Item(index), errors))
            .ToList()),
        YamlScalarNode scalar => ConvertScalar(scalar, path, errors),
        _ => throw new InvalidOperationException($"Unexpected YAML node type '{node.GetType().Name}'"),
    };

    private static ConfigurationScalar ConvertScalar(YamlScalarNode scalar, ConfigurationNodePath path, ConfigurationErrors errors)
    {
        var plain = scalar.Style is ScalarStyle.Plain or ScalarStyle.Any;
        var style = plain ? ConfigurationScalarStyle.Plain : ConfigurationScalarStyle.Quoted;

        if (plain && NullScalars.Contains(scalar.Value ?? string.Empty))
            return new ConfigurationScalar(path, null, style, IsSecret: false);

        var value = ConfigurationInterpolator.Interpolate(scalar.Value ?? string.Empty, path, errors, out var interpolated);
        return new ConfigurationScalar(path, value, style, interpolated);
    }
}
