namespace Runax.Hookpipe.Configuration.Sources;

/// <summary>
/// How a scalar was written, which decides whether <c>100</c> is a number or text.
/// </summary>
internal enum ConfigurationScalarStyle
{
    /// <summary>A plain YAML scalar: <c>100</c> is a number and <c>true</c> a boolean.</summary>
    Plain,

    /// <summary>A quoted or block YAML scalar: always text.</summary>
    Quoted,

    /// <summary>A value with no type, as every <c>IConfiguration</c> value is.</summary>
    Untyped,
}
