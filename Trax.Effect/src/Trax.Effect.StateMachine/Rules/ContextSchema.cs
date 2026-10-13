namespace Trax.Effect.StateMachine;

/// <summary>
/// One field of a state's context, a trigger's input or a train's output: its JSON key, JSON type, whether it
/// may be null, and any extra constraints (each a <see cref="Rule"/> over this field, e.g. non-empty from
/// <c>[MinLength(1)]</c>).
/// </summary>
public sealed record FieldSchema(
    string Name,
    JsonFieldType Type,
    bool Nullable,
    IReadOnlyList<Rule> Constraints
)
{
    /// <summary>
    /// The shape of a nested record: the fields of an <see cref="JsonFieldType.Object"/> field, or of every
    /// element of an <see cref="JsonFieldType.Array"/> field. Null when the field has no declared shape (a
    /// scalar, a list of scalars, a dictionary, or a context field, which is described one level deep).
    /// </summary>
    public IReadOnlyList<FieldSchema>? Fields { get; init; }

    /// <summary>
    /// The schema every value of a dictionary field, or every element of a list field whose elements are not
    /// records, is checked against: its JSON type, whether it may be null, its range, and its own
    /// <see cref="Fields"/> or <see cref="Values"/>. Its <see cref="Name"/> is empty and it has no
    /// <see cref="Constraints"/>. Null for a field that is neither.
    /// </summary>
    public FieldSchema? Values { get; init; }

    /// <summary>
    /// The schema every key of a dictionary field is checked against, from its CLR key type: an
    /// <see cref="JsonFieldType.Integer"/> key is an integer's digits within <see cref="Minimum"/> and
    /// <see cref="Maximum"/>, and a <see cref="JsonFieldType.String"/> key carries an enum's names or a format. Its
    /// <see cref="Name"/> is empty and it has no <see cref="Constraints"/>. Null for a dictionary keyed by string,
    /// and for any other field.
    /// </summary>
    public FieldSchema? Keys { get; init; }

    /// <summary>
    /// The smallest value a number field accepts: its CLR type's minimum (an integral type's, a decimal's, a
    /// float's or a Half's), or past 2^53 the nearest double inside it. Null when the field is not range-checked.
    /// </summary>
    public double? Minimum { get; init; }

    /// <summary>
    /// The largest value a number field accepts: its CLR type's maximum, or past 2^53 the nearest double inside
    /// it. Null when the field is not range-checked.
    /// </summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// The names a <see cref="JsonFieldType.String"/> field from a CLR enum may hold, as the machine's JSON writes
    /// them (camelCase, or a member's <c>[JsonStringEnumMemberName]</c>). A name is compared ignoring ASCII case,
    /// as System.Text.Json reads a member's own name, unless it is one of <see cref="EnumExact"/>. Null for any
    /// other field.
    /// </summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>
    /// The names of <see cref="Enum"/> that match only as written: a member renamed with
    /// <c>[JsonStringEnumMemberName]</c>, which System.Text.Json reads case-sensitively. Null when the enum renames
    /// no member, and for any other field.
    /// </summary>
    public IReadOnlyList<string>? EnumExact { get; init; }

    /// <summary>
    /// The shape a <see cref="JsonFieldType.String"/> field's value has, from its CLR type: <c>uuid</c> (a Guid),
    /// <c>date-time</c> (a DateTime), <c>date-time-offset</c> (a DateTimeOffset), <c>date</c> (a DateOnly),
    /// <c>time</c> (a TimeOnly),
    /// <c>duration</c> (a TimeSpan, as the ISO-8601 duration a timer reads), <c>uri</c> (an absolute Uri) or
    /// <c>char</c> (one UTF-16 code unit). See <see cref="ValueFormats"/>. Null for any other field.
    /// </summary>
    public string? Format { get; init; }
}

/// <summary>
/// The declarative shape of a state's context, a trigger's input or a train's output: the fields it carries,
/// sorted by key (ordinal) to match the canonical wire. Reflected from the author's record
/// (<see cref="SchemaReflection"/>), carried in the IR, and turned into a shape validator by each engine.
/// </summary>
public sealed record ContextSchema(IReadOnlyList<FieldSchema> Fields)
{
    /// <summary>A state with no context, or a trigger that takes no input.</summary>
    public static readonly ContextSchema Empty = new(Array.Empty<FieldSchema>());
}
