using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>Where a <see cref="SetStep"/> takes its value from.</summary>
public abstract record ValueSource
{
    /// <summary>Copy a field out of the trigger input.</summary>
    public sealed record FromInput(string Field) : ValueSource;

    /// <summary>A literal JSON value.</summary>
    public sealed record Constant(JsonNode? Value) : ValueSource;

    private ValueSource() { }
}

/// <summary>One field assignment in a <see cref="Reduction.Set"/>.</summary>
public sealed record SetStep(string Field, ValueSource Source);

/// <summary>
/// A declarative reducer: how a transition produces the destination context. Data, like <see cref="Rule"/>,
/// so the same reduction drives the runtime, the IR, and the generated per-language code. Reducers must be
/// pure functions of (context, input); anything nondeterministic (a receipt, a timestamp) arrives as input.
/// The genuinely-custom case is a <see cref="Custom"/> handler referenced by name.
/// </summary>
public abstract record Reduction
{
    /// <summary>Carry the current context forward unchanged (the default when no reducer is declared).</summary>
    public sealed record Keep : Reduction;

    /// <summary>Produce an empty context.</summary>
    public sealed record Clear : Reduction;

    /// <summary>Produce the machine's initial context (schema defaults / the start factory).</summary>
    public sealed record Reset : Reduction;

    /// <summary>Clone the current context and apply field assignments (the clone-and-set case).</summary>
    public sealed record Set(IReadOnlyList<SetStep> Steps) : Reduction;

    /// <summary>A named custom reducer, hand-written per runtime and differential-guarded.</summary>
    public sealed record Custom(string Name) : Reduction;

    private Reduction() { }
}
