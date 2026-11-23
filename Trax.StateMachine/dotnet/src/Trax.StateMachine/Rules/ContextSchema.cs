namespace Trax.StateMachine;

/// <summary>
/// The declarative shape of a state's context, a trigger's input or a train's output: the fields it carries,
/// sorted by key (ordinal) to match the canonical wire. Reflected from the author's record
/// (<see cref="SchemaReflection"/>), carried in the IR, and turned into a shape validator by each engine.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/declarative-data-model#context-schema">SDK reference</seealso>
public sealed record ContextSchema(IReadOnlyList<FieldSchema> Fields)
{
    /// <summary>A state with no context, or a trigger that takes no input.</summary>
    public static readonly ContextSchema Empty = new(Array.Empty<FieldSchema>());
}
