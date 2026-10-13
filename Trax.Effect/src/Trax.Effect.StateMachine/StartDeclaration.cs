namespace Trax.Effect.StateMachine;

/// <summary>
/// A machine's declared <c>start</c>: the record a client starts an instance with, its schema, and the reduction that
/// turns it into the initial context. Who may start one is the start's rule in <c>MachineRules.Start</c>. Recorded by
/// <see cref="IMachineBuilder{TState,TTrigger}.Start{TStartInput}"/>.
/// </summary>
/// <param name="InputType">The start input record.</param>
/// <param name="InputSchema">The schema the start input is validated against, reflected from <paramref name="InputType"/>.</param>
/// <param name="Reduction">How the start input becomes the initial context; <see cref="Reduction.Keep"/> when none is declared.</param>
internal sealed record StartDeclaration(
    Type InputType,
    ContextSchema InputSchema,
    Reduction Reduction
)
{
    /// <summary>Applies <see cref="Reduction"/> to the initial context with the start input; never throws.</summary>
    public Func<
        System.Text.Json.Nodes.JsonObject,
        System.Text.Json.Nodes.JsonNode?,
        System.Text.Json.Nodes.JsonObject
    > Apply { get; init; } = static (ctx, _) => (System.Text.Json.Nodes.JsonObject)ctx.DeepClone();

    /// <summary>
    /// The context fields the start input reaches: every field a <see cref="Reduction.Set"/> step copies from the
    /// input. Null when the reduction is opaque (a custom reducer), which may reach any field.
    /// </summary>
    public IReadOnlyCollection<string>? InputFields => FieldsFromInput(Reduction);

    /// <summary>
    /// The top-level context fields <paramref name="reduction"/> copies from the input: every field a
    /// <see cref="Reduction.Set"/> step reads from it. Null for an opaque (custom) reducer, which may reach any; empty
    /// for none.
    /// </summary>
    internal static IReadOnlyCollection<string>? FieldsFromInput(Reduction? reduction) =>
        reduction switch
        {
            Reduction.Set set => set
                .Steps.Where(s => s.Source is ValueSource.FromInput)
                .Select(s => TopLevel(s.Field))
                .ToHashSet(StringComparer.Ordinal),
            Reduction.Custom => null,
            _ => Array.Empty<string>(),
        };

    internal static string TopLevel(string field)
    {
        var dot = field.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? field : field[..dot];
    }
}

/// <summary>
/// A write only the server makes to an instance's context: an invoked train's outcome, the machine's effect, or an
/// event gated by <c>Requires</c>. A start may not reach a field one of these writes.
/// </summary>
/// <param name="Source">What makes the write, as a refusal names it.</param>
/// <param name="Fields">The context fields it writes, or null when its reducer is opaque and may write any.</param>
internal sealed record ServerWrite(string Source, IReadOnlyCollection<string>? Fields)
{
    /// <summary>The server write a transition's reducer makes, from its declarative reduction or its opaque delegate.</summary>
    public static ServerWrite Of(string source, Reduction? reduction, bool opaque) =>
        new(
            source,
            opaque
                ? null
                : reduction switch
                {
                    Reduction.Set set => set
                        .Steps.Select(s => StartDeclaration.TopLevel(s.Field))
                        .ToHashSet(StringComparer.Ordinal),
                    Reduction.Custom => null,
                    _ => Array.Empty<string>(),
                }
        );
}
