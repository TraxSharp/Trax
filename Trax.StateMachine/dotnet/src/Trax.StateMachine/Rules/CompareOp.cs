namespace Trax.StateMachine;

/// <summary>Numeric and count comparison operators.</summary>
public enum CompareOp
{
    /// <summary>The field's value is strictly greater than the constant.</summary>
    GreaterThan,

    /// <summary>The field's value is greater than or equal to the constant.</summary>
    GreaterOrEqual,

    /// <summary>The field's value is strictly less than the constant.</summary>
    LessThan,

    /// <summary>The field's value is less than or equal to the constant.</summary>
    LessOrEqual,

    /// <summary>The field's value equals the constant.</summary>
    EqualTo,
}
