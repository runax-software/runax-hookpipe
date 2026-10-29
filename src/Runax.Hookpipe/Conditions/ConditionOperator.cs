namespace Runax.Hookpipe.Conditions;

/// <summary>
/// How a comparison matches the selected value.
/// </summary>
public enum ConditionOperator
{
    /// <summary>The value equals the operand.</summary>
    Eq,

    /// <summary>The value doesn't equal the operand. False when nothing is selected.</summary>
    Ne,

    /// <summary>The value equals one of the operands.</summary>
    In,

    /// <summary>The value equals none of the operands. False when nothing is selected.</summary>
    NotIn,

    /// <summary>The number is greater than the operand.</summary>
    Gt,

    /// <summary>The number is greater than or equal to the operand.</summary>
    Gte,

    /// <summary>The number is less than the operand.</summary>
    Lt,

    /// <summary>The number is less than or equal to the operand.</summary>
    Lte,

    /// <summary>The text contains the operand, ignoring case.</summary>
    Contains,

    /// <summary>The text starts with the operand, ignoring case.</summary>
    StartsWith,

    /// <summary>The text ends with the operand, ignoring case.</summary>
    EndsWith,

    /// <summary>The .NET regex in the operand matches the text (case-sensitive).</summary>
    Regex,

    /// <summary>
    /// With operand <c>true</c>, a non-null value is selected; with <c>false</c>, none is.
    /// </summary>
    Exists,
}
