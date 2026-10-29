namespace Runax.Hookpipe.Conditions;

/// <summary>
/// What kind of value a condition compares with, which decides how equality works.
/// </summary>
public enum ConditionValueKind
{
    /// <summary>Text. Compared with the selected value's text; a JSON number's text is its literal.</summary>
    Text,

    /// <summary>A number. Compared by value, so <c>100</c> matches <c>100.0</c>.</summary>
    Number,

    /// <summary>A boolean. Matches JSON <c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>
    /// A value with no type, as <c>IConfiguration</c> gives. Matches when the text is equal or both
    /// sides read as the same number or boolean.
    /// </summary>
    Untyped,
}
