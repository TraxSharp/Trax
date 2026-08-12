namespace Trax.Effect.StateMachine;

/// <summary>
/// One field of a state's context: its JSON key, JSON type, whether it may be null, and any extra
/// constraints (each a <see cref="Rule"/> over this field, e.g. non-empty from <c>[MinLength(1)]</c>).
/// </summary>
public sealed record FieldSchema(
    string Name,
    JsonFieldType Type,
    bool Nullable,
    IReadOnlyList<Rule> Constraints
);

/// <summary>
/// The declarative shape of a state's context: the fields it carries, sorted by key (ordinal) to match the
/// canonical wire. Reflected from the author's context record (<see cref="SchemaReflection"/>), carried in
/// the IR, and turned into a shape validator by each generator.
/// </summary>
public sealed record ContextSchema(IReadOnlyList<FieldSchema> Fields)
{
    /// <summary>A state with no context.</summary>
    public static readonly ContextSchema Empty = new(Array.Empty<FieldSchema>());
}
