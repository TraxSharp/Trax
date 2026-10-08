using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Declares where the outcome of a state's invoked train goes. Returned by
/// <see cref="IStateBuilder{TState,TTrigger}.Invokes{TTrain,TInput,TOutput}"/>; it is also the state's builder,
/// so the state's other transitions follow it.
///
/// <para>Every invoking state needs one or more <see cref="OnDone"/> edges, exactly one <see cref="OnFailed"/>
/// and exactly one <see cref="OnCancelled"/>; <see cref="MachineBuilder{TState,TTrigger}.Build"/> refuses a
/// state missing either of the last two, naming it. Every target joins the machine's reserved states, and no
/// ordinary transition may enter one.</para>
/// </summary>
public interface IInvokeBuilder<TState, TTrigger> : IStateBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// When the run succeeds, go to <paramref name="target"/>. Declare several to route on the output: they are
    /// tried in declaration order and the first whose <paramref name="when"/> holds is taken, so an unguarded
    /// edge after the guarded ones is the fallback. To the engine, an output no edge accepts moves nothing (a typed
    /// <c>no-transition</c>); on the server the run has finished, so such an output is applied as the state's
    /// <see cref="OnFailed"/>, with the reason <c>invoke-output-unaccepted</c>.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="when">
    /// A guard over the train's output, which is this outcome's input: write it with
    /// <c>Rules.Input((MyOutput o) =&gt; o.Unsure).IsTrue()</c>. Null takes the edge for every output.
    /// </param>
    /// <param name="reduce">
    /// How the output becomes the target's context, e.g.
    /// <c>Rules.Set((Ctx c) =&gt; c.Fingerprint).FromInput((MyOutput o) =&gt; o.Fingerprint)</c>. Null keeps the
    /// context. A snapshot holds pointers, not data: reduce a fingerprint or a URI, never rows.
    /// </param>
    IInvokeBuilder<TState, TTrigger> OnDone(
        TState target,
        Rule? when = null,
        Reduction? reduce = null
    );

    /// <summary>
    /// When the run fails, including a run the scheduler reaps, go to <paramref name="target"/>. Declare it
    /// exactly once. The scheduler never retries an invoked run; a machine retries by entering the invoking state
    /// again.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="reduce">How the context changes; null keeps it. The outcome carries no input.</param>
    IInvokeBuilder<TState, TTrigger> OnFailed(TState target, Reduction? reduce = null);

    /// <summary>
    /// When the run is cancelled (a timeout, or an operator's cancel), go to <paramref name="target"/>. Required,
    /// and declared exactly once.
    /// </summary>
    /// <param name="target">The state the machine enters.</param>
    /// <param name="reduce">How the context changes; null keeps it. The outcome carries no input.</param>
    IInvokeBuilder<TState, TTrigger> OnCancelled(TState target, Reduction? reduce = null);
}

/// <summary>How an invoked run ended. Its outcome trigger is named <c>&lt;State&gt;.done</c>, <c>.failed</c> or <c>.cancelled</c>.</summary>
internal enum InvokeOutcomeKind
{
    Done,
    Failed,
    Cancelled,
}

/// <summary>
/// One run's outcome, as the persistence layer hands it to <see cref="SnapshotMachine{TState,TTrigger}.ApplyOutcome"/>.
/// </summary>
internal abstract record InvokeOutcome
{
    public abstract InvokeOutcomeKind Kind { get; }

    /// <summary>The run succeeded; <see cref="Output"/> is its output serialized with <see cref="InvokeDefinition{TState}.SerializeOutput"/>.</summary>
    public sealed record Done(JsonNode? Output) : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Done;
    }

    /// <summary>The run failed.</summary>
    public sealed record Failed : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Failed;
    }

    /// <summary>The run was cancelled.</summary>
    public sealed record Cancelled : InvokeOutcome
    {
        public override InvokeOutcomeKind Kind => InvokeOutcomeKind.Cancelled;
    }

    private InvokeOutcome() { }
}

/// <summary>
/// One edge an outcome can take: its target, its declarative guard and reduction (exported to the IR), and the
/// compiled forms the engine runs.
/// </summary>
internal sealed record OutcomeEdge<TState>(
    TState To,
    Rule? Guard,
    Reduction? Reduce,
    Func<JsonObject, JsonNode?, bool>? CompiledGuard,
    Func<JsonObject, JsonNode?, JsonObject>? CompiledReduce
)
    where TState : struct, Enum;

/// <summary>
/// A state's invoked train and where each outcome goes. The input mapping is server-only: the TypeScript twin
/// never starts a train, so <see cref="CreateInput"/> is never exported.
/// </summary>
internal sealed record InvokeDefinition<TState>(
    TState State,
    Type TrainType,
    Type InputType,
    Type OutputType,
    Func<JsonObject, object?> CreateInput,
    IReadOnlyList<OutcomeEdge<TState>> Done,
    OutcomeEdge<TState> Failed,
    OutcomeEdge<TState> Cancelled
)
    where TState : struct, Enum
{
    /// <summary>The train's canonical name: its interface's full name.</summary>
    public string TrainName => TrainType.FullName ?? TrainType.Name;

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
    /// same options a run stores its output with for the machine (<see cref="Utils.InvokedRunOutput"/>), so the
    /// fields a guard or reduction names exist in the output whichever produced it.
    /// </remarks>
    public JsonNode? SerializeOutput(object? output) =>
        JsonSerializer.SerializeToNode(output, OutputType, Utils.InvokedRunOutput.Json);
}

/// <summary>
/// Names outcome triggers. A trigger enum member cannot contain a dot, so <c>&lt;State&gt;.done</c> never collides
/// with a user's trigger, and the TypeScript twin builds the same names from the IR.
/// </summary>
internal static class OutcomeTriggers
{
    public static string Name<TState>(TState state, InvokeOutcomeKind kind)
        where TState : struct, Enum => $"{state}.{Suffix(kind)}";

    public static string Suffix(InvokeOutcomeKind kind) =>
        kind switch
        {
            InvokeOutcomeKind.Done => "done",
            InvokeOutcomeKind.Failed => "failed",
            _ => "cancelled",
        };

    /// <summary>Splits <c>&lt;State&gt;.&lt;kind&gt;</c>; false for anything else.</summary>
    public static bool TryParse(string trigger, out string state, out InvokeOutcomeKind kind)
    {
        state = "";
        kind = default;
        var dot = trigger.LastIndexOf('.');
        if (dot <= 0)
            return false;
        state = trigger[..dot];
        switch (trigger[(dot + 1)..])
        {
            case "done":
                kind = InvokeOutcomeKind.Done;
                return true;
            case "failed":
                kind = InvokeOutcomeKind.Failed;
                return true;
            case "cancelled":
                kind = InvokeOutcomeKind.Cancelled;
                return true;
            default:
                return false;
        }
    }
}
