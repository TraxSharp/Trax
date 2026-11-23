namespace Trax.StateMachine;

/// <summary>The JSON kinds a field can be constrained to.</summary>
public enum JsonFieldType
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number (integer or fractional).</summary>
    Number,

    /// <summary>JSON <see langword="true"/> or <see langword="false"/>.</summary>
    Boolean,

    /// <summary>A JSON array.</summary>
    Array,

    /// <summary>A JSON object.</summary>
    Object,

    /// <summary>A JSON number with no fractional part (<c>2020</c> and <c>2020.0</c> both count).</summary>
    Integer,
}
