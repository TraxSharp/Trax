using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>A single transition: its guard, message, reducer, optional exactly-once effect, and destination.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#itransitionbuilder">SDK reference</seealso>
public interface ITransitionBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Only take this edge when the predicate holds. Guards for one (state, trigger) must be mutually exclusive.</summary>
    ITransitionBuilder<TState, TTrigger> When(Func<JsonObject, JsonNode?, bool> guard);

    /// <summary>Only take this edge when the declarative <see cref="Rule"/> holds (the exportable, string-free guard).</summary>
    ITransitionBuilder<TState, TTrigger> When(Rule guard);

    /// <summary>
    /// Declare this trigger's input as the record <typeparamref name="TInput"/>. Both engines check every input
    /// sent with the trigger against the record's schema before any guard reads it, as strictly as the CLR type:
    /// a missing or wrongly typed field, a fraction in an integral field, a field the record does not declare, or
    /// anything other than an object is refused as <c>INVALID_INPUT</c>. The record's <c>[JsonPropertyName]</c>
    /// names are honoured and its <c>[JsonIgnore]</c> members are not part of the input. On a declarative machine
    /// a trigger without <c>WithInput</c> takes no input: only none or <c>{}</c> is accepted.
    /// </summary>
    /// <typeparam name="TInput">The input record. Every edge of one trigger declares the same record, or none does.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TInput"/> contains itself, or has a member with no fixed JSON type.
    /// </exception>
    ITransitionBuilder<TState, TTrigger> WithInput<TInput>();

    /// <summary>A human-readable reason surfaced when the guard declines (non-contract detail text).</summary>
    ITransitionBuilder<TState, TTrigger> Because(string guardMessage);

    /// <summary>Produce the destination state's context. Omit to carry the current context forward unchanged.</summary>
    ITransitionBuilder<TState, TTrigger> Reduce(Func<JsonObject, JsonNode?, JsonObject> reduce);

    /// <summary>Produce the destination context with a declarative <see cref="Reduction"/> (the exportable reducer).</summary>
    ITransitionBuilder<TState, TTrigger> Reduce(Reduction reduce);

    /// <summary>
    /// Bind the one irreversible effect to this transition. It runs exactly-once (claim before the effect,
    /// lease + fence, crash-retry replays) and its receipt is available to the reducer as
    /// <c>input["receipt"]</c>. The effect implementation is resolved from DI by <typeparamref name="TEffect"/>.
    /// A machine binds at most one effect: <see cref="MachineBuilder{TState,TTrigger}.Build"/> refuses a second.
    /// </summary>
    ITransitionBuilder<TState, TTrigger> RunsOnce<TEffect>(string? keyPrefix = null);

    /// <summary>Finish the transition: land in <paramref name="target"/>. Returns the state builder for more transitions.</summary>
    IStateBuilder<TState, TTrigger> To(TState target);

    /// <summary>
    /// Only an authenticated caller in at least one of <paramref name="roles"/> may fire this event, compared
    /// ordinally. Chained calls each add a set the caller must also meet. The draft service checks it against every
    /// client actor before the request id, the input or the guard, and refuses one that fails it as
    /// <c>FORBIDDEN</c>. An event has one rule: every edge of it declares the same one, and an event that requires a
    /// role may not enter the start state.
    /// </summary>
    ITransitionBuilder<TState, TTrigger> Requires(params string[] roles) =>
        throw new NotSupportedException($"{GetType().Name} does not support Requires.");

    /// <summary>
    /// Only an authenticated caller who satisfies the authorization policy <paramref name="policy"/> may fire this
    /// event. The host refuses to start when the policy is not registered.
    /// </summary>
    ITransitionBuilder<TState, TTrigger> RequiresPolicy(string policy) =>
        throw new NotSupportedException($"{GetType().Name} does not support RequiresPolicy.");

    /// <summary>Any authenticated caller may fire this event.</summary>
    ITransitionBuilder<TState, TTrigger> AnyAuthenticated() =>
        throw new NotSupportedException($"{GetType().Name} does not support AnyAuthenticated.");

    /// <summary>
    /// Any caller may fire this event, authenticated or not. Refused at startup on a user-owned machine a client
    /// can reach, whose instances belong to a user.
    /// </summary>
    ITransitionBuilder<TState, TTrigger> Anonymous() =>
        throw new NotSupportedException($"{GetType().Name} does not support Anonymous.");
}
