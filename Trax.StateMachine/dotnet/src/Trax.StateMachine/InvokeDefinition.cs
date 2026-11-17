using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// A state's invoked train and where each outcome goes. The input mapping is server-only: the TypeScript twin
/// never starts a train, so <see cref="CreateInput"/> is never exported. It is given the context the state is entered
/// with and the instance owner's user key, null for a system instance.
/// </summary>
internal sealed record InvokeDefinition<TState>(
    TState State,
    Type TrainType,
    Type InputType,
    Type OutputType,
    Func<JsonObject, string?, object?> CreateInput,
    IReadOnlyList<OutcomeEdge<TState>> Done,
    OutcomeEdge<TState> Failed,
    OutcomeEdge<TState> Cancelled
)
    where TState : struct, Enum
{
    /// <summary>The train's canonical name: its interface's full name.</summary>
    public string TrainName => TrainType.FullName ?? TrainType.Name;

    /// <summary>
    /// The schema of <see cref="OutputType"/>, which the success outcome's input is checked against before any
    /// <c>OnDone</c> guard reads it. Reflected when the machine is built.
    /// </summary>
    public ContextSchema OutputSchema { get; init; } = ContextSchema.Empty;

    /// <summary>
    /// The child machine this state starts instead of a train, or null when it invokes one. For a spawning state
    /// <see cref="TrainType"/> is the child machine's class and <see cref="CreateInput"/> is never called.
    /// </summary>
    public SpawnDefinition? Spawn { get; init; }

    /// <summary>The outcome trigger for <paramref name="kind"/>, e.g. <c>Fetching.done</c>.</summary>
    public string TriggerName(InvokeOutcomeKind kind) => OutcomeTriggers.Name(State, kind);

    /// <summary>Every target an outcome can reach, in declaration order, without repeats.</summary>
    public IEnumerable<TState> Targets =>
        Done.Select(e => e.To).Append(Failed.To).Append(Cancelled.To).Distinct();

    /// <summary>The edges an outcome of <paramref name="kind"/> tries, in order.</summary>
    public IReadOnlyList<OutcomeEdge<TState>> EdgesFor(InvokeOutcomeKind kind) =>
        kind switch
        {
            InvokeOutcomeKind.Done => Done,
            InvokeOutcomeKind.Failed => [Failed],
            _ => [Cancelled],
        };

    /// <summary>Serializes a run's output into the JSON an outcome's guard and reduction read.</summary>
    /// <remarks>
    /// The same naming the declarative schema reflects (camelCase members, enums as camelCase strings), and the
    /// same options a run stores its output with for the machine (<see cref="Trax.Effect.Utils.InvokedRunOutput"/>), so the
    /// fields a guard or reduction names exist in the output whichever produced it.
    /// </remarks>
    public JsonNode? SerializeOutput(object? output) =>
        JsonSerializer.SerializeToNode(output, OutputType, Trax.Effect.Utils.InvokedRunOutput.Json);
}
