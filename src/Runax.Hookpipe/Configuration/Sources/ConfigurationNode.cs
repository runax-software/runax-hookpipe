namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// One node of a configuration source, read into the same shape whatever the source is, so one binder
/// turns every source into the model.
/// </summary>
/// <param name="Path">Where the node sits, for error messages.</param>
internal abstract record ConfigurationNode(ConfigurationNodePath Path);
