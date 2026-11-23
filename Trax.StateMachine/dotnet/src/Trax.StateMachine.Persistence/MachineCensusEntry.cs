namespace Trax.StateMachine.Persistence;

/// <summary>One trigger or read of a machine, as <see cref="MachineCensus.Describe"/> lists it.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="Name">The trigger's name, the read's (<c>start</c>, <c>get</c>, the push (<c>&lt;machine&gt;Changed</c>)), or <c>discard</c>.</param>
/// <param name="Kind">The trigger's <see cref="TriggerKind"/> name, <c>Read</c> for a read, or <c>Discard</c>.</param>
/// <param name="Rule">The rule that guards it, as a reader would write it.</param>
/// <param name="Surfaces">The surfaces that reach it, such as the machine's own operations or host code.</param>
public sealed record MachineCensusEntry(
    string Machine,
    string Name,
    string Kind,
    string Rule,
    IReadOnlyList<string> Surfaces
);
