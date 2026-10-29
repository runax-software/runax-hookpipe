using System.Globalization;

namespace Runax.Hookpipe.Conditions;

/// <summary>
/// A value a condition compares with, such as the <c>paid</c> in <c>eq: paid</c>.
/// </summary>
/// <param name="Text">The value's text. Booleans are <c>true</c> or <c>false</c>.</param>
/// <param name="Kind">The value's kind.</param>
public readonly record struct ConditionValue(string Text, ConditionValueKind Kind)
{
    /// <summary>
    /// A text value.
    /// </summary>
    public static ConditionValue Of(string text) => new(text, ConditionValueKind.Text);

    /// <summary>
    /// A number value.
    /// </summary>
    public static ConditionValue Of(decimal number) =>
        new(number.ToString(CultureInfo.InvariantCulture), ConditionValueKind.Number);

    /// <summary>
    /// A boolean value.
    /// </summary>
    public static ConditionValue Of(bool value) => new(value ? "true" : "false", ConditionValueKind.Boolean);

    internal bool TryGetNumber(out decimal number) => TryParseNumber(Text, out number);

    internal bool TryGetBoolean(out bool value) => bool.TryParse(Text, out value);

    internal static bool TryParseNumber(string? text, out decimal number) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
}
