namespace Trax.StateMachine;

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
