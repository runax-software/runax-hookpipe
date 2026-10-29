namespace Runax.Hookpipe.Conditions;

/// <summary>
/// Where a comparison reads its value from.
/// </summary>
public enum ConditionSource
{
    /// <summary>The JSON body, through an RFC 9535 JSONPath such as <c>$.data.status</c>.</summary>
    JsonPath,

    /// <summary>A request header; the name is case-insensitive.</summary>
    Header,

    /// <summary>A path parameter of the webhook's route.</summary>
    Path,
}
