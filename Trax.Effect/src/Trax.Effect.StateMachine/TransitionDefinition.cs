using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// One edge of a <see cref="MachineDefinition{TState,TTrigger}"/>: "from <see cref="From"/>, on
/// <see cref="Trigger"/>, go to <see cref="To"/>".
///
/// <para><see cref="Guard"/> and <see cref="Reduce"/> are the SCXML-inspired hooks kept as
/// <b>named code</b> rather than serialized expressions: the JSON snapshot carries no logic, so
/// there is no shared expression language to evaluate on two runtimes. Both operate on the current
/// context and the trigger's optional input.</para>
/// </summary>
public sealed class TransitionDefinition<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>The source state.</summary>
    public required TState From { get; init; }

    /// <summary>The trigger that fires this edge.</summary>
    public required TTrigger Trigger { get; init; }

    /// <summary>The destination state (may equal <see cref="From"/> for a self-transition).</summary>
    public required TState To { get; init; }

    /// <summary>
    /// Optional predicate over (current context, trigger input). When it returns false the
    /// transition is declined with <see cref="RejectionReasons.GuardFailed"/>. Guards for the same
    /// (state, trigger) must be mutually exclusive — at most one may pass.
    /// </summary>
    public Func<JsonObject, JsonNode?, bool>? Guard { get; init; }

    /// <summary>
    /// Optional human-readable explanation surfaced as the rejection detail when this transition's
    /// guard declines — so a <c>guard-failed</c> can say "recipients must be non-empty" instead of a
    /// generic message. Useful both for debugging and as the GraphQL rejection detail. This text is
    /// NOT part of the cross-language contract (only the reason code is).
    /// </summary>
    public string? GuardMessage { get; init; }

    /// <summary>
    /// Optional producer of the <see cref="To"/> state's context from (current context, trigger
    /// input). When omitted the current context is carried forward unchanged. Must return a fresh
    /// <see cref="JsonObject"/> that survives a JSON round-trip.
    /// </summary>
    public Func<JsonObject, JsonNode?, JsonObject>? Reduce { get; init; }
}
