namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// A list of nodes.
/// </summary>
/// <param name="Path">Where the list sits.</param>
/// <param name="Items">The items, in order.</param>
internal sealed record ConfigurationSequence(ConfigurationNodePath Path, IReadOnlyList<ConfigurationNode> Items) : ConfigurationNode(Path);
