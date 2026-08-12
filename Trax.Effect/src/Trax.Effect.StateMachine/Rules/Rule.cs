namespace Trax.Effect.StateMachine;

/// <summary>Where a rule reads its field from: the snapshot's context, or the trigger's input.</summary>
public enum RuleSource
{
    Context,
    Input,
}

/// <summary>The JSON kinds a field can be constrained to.</summary>
public enum JsonFieldType
{
    String,
    Number,
    Boolean,
    Array,
    Object,
}

/// <summary>Numeric and count comparison operators.</summary>
public enum CompareOp
{
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    EqualTo,
}

/// <summary>
/// A declarative guard/validator predicate over a snapshot's context and a trigger's input. Rules are
/// <b>data</b>, not closures: the engine evaluates them (<see cref="RuleEvaluator"/>), the IR exporter emits
/// them, and the per-language generators turn them into native validators. The same predicate therefore
/// drives the runtime, the cross-language contract, and the generated code from one source.
///
/// <para>A predicate too complex to express here is a <see cref="Custom"/> handler referenced by name and
/// hand-written per runtime (the ~8% escape hatch). Field names are strings at this layer because they are
/// JSON keys; the authoring surface supplies them from strongly-typed member expressions.</para>
/// </summary>
public abstract record Rule
{
    /// <summary>The field exists and is not JSON null.</summary>
    public sealed record Present(RuleSource Source, string Field) : Rule;

    /// <summary>The field is missing or JSON null.</summary>
    public sealed record Absent(RuleSource Source, string Field) : Rule;

    /// <summary>The field is present and of the given JSON type.</summary>
    public sealed record OfType(RuleSource Source, string Field, JsonFieldType Type) : Rule;

    /// <summary>A non-empty string (length &gt; 0) or a non-empty array (count &gt; 0).</summary>
    public sealed record NonEmpty(RuleSource Source, string Field) : Rule;

    /// <summary>A string field whose value is one of a fixed set.</summary>
    public sealed record OneOf(RuleSource Source, string Field, IReadOnlyList<string> Values)
        : Rule;

    /// <summary>A numeric field compared against a constant.</summary>
    public sealed record Compare(RuleSource Source, string Field, CompareOp Op, double Value)
        : Rule;

    /// <summary>An array field whose length is compared against a constant.</summary>
    public sealed record Count(RuleSource Source, string Field, CompareOp Op, int Value) : Rule;

    /// <summary>A string field whose length is compared against a constant.</summary>
    public sealed record Length(RuleSource Source, string Field, CompareOp Op, int Value) : Rule;

    /// <summary>A boolean field equal to a constant.</summary>
    public sealed record BoolEquals(RuleSource Source, string Field, bool Value) : Rule;

    /// <summary>An array field whose every element is of the given JSON type.</summary>
    public sealed record ArrayOf(RuleSource Source, string Field, JsonFieldType ElementType) : Rule;

    /// <summary>All sub-rules hold (logical AND). An empty list is vacuously true.</summary>
    public sealed record All(IReadOnlyList<Rule> Rules) : Rule;

    /// <summary>At least one sub-rule holds (logical OR). An empty list is false.</summary>
    public sealed record Any(IReadOnlyList<Rule> Rules) : Rule;

    /// <summary>A named custom predicate, hand-written per runtime and differential-guarded.</summary>
    public sealed record Custom(string Name) : Rule;

    private Rule() { }
}
