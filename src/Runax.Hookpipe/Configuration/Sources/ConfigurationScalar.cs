namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// A single value. <see cref="Value"/> is null for YAML's <c>null</c>, <c>~</c>, or an empty value.
/// </summary>
/// <param name="Path">Where the value sits.</param>
/// <param name="Value">The value after <c>${…}</c> interpolation.</param>
/// <param name="Style">How the value was written.</param>
/// <param name="IsSecret">
/// Whether interpolation filled anything in. Such values never appear in errors or logs.
/// </param>
internal sealed record ConfigurationScalar(ConfigurationNodePath Path, string? Value, ConfigurationScalarStyle Style, bool IsSecret)
    : ConfigurationNode(Path)
{
    /// <summary>
    /// The value quoted for an error message, or a placeholder when it's secret.
    /// </summary>
    public string Describe() => IsSecret ? "the interpolated value" : $"'{Value}'";
}
