using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>Per-state configuration: its context validator, whether it is committed, and its transitions.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#istatebuilder">SDK reference</seealso>
public interface IStateBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>The context validator for this state (returns null when valid, else a message). Makes illegal states unrepresentable.</summary>
    IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator);

    /// <summary>
    /// Declare this state's context shape from a record: the fields, JSON types, nullability, and
    /// attribute-derived constraints (e.g. <c>[MinLength(1)]</c>) become the validator AND the exportable
    /// schema. The declarative, string-free replacement for <see cref="Holds"/>.
    /// </summary>
    IStateBuilder<TState, TTrigger> Context<TContext>();

    /// <summary>Declare that this state carries no context (an empty schema).</summary>
    IStateBuilder<TState, TTrigger> Context();

    /// <summary>
    /// Add a per-state context requirement, composed (ANDed) with the schema from
    /// <see cref="Context{TContext}"/> and with any other requirements. The declarative, exportable peer of a
    /// policy check in <see cref="Holds"/>: use it for what a state demands beyond its shape (a complete draft,
    /// an absent receipt). Chain several; each is one small rule.
    /// </summary>
    IStateBuilder<TState, TTrigger> Requires(Rule constraint);

    /// <summary>Mark this state committed: a soft autosave may not move a draft out of it (the guarded path).</summary>
    IStateBuilder<TState, TTrigger> Committed();

    /// <summary>Begin a transition out of this state on a trigger.</summary>
    ITransitionBuilder<TState, TTrigger> On(TTrigger trigger);

    /// <summary>
    /// Run a train whenever the machine enters this state, and route its outcome with the returned builder's
    /// <c>OnDone</c>, <c>OnFailed</c> and <c>OnCancelled</c>. A state invokes at most one train. The run belongs
    /// to the state: leaving the state cancels it, and its outcome is applied only to the entry that queued it.
    /// The scheduler never retries it; a machine retries by entering the state again.
    ///
    /// <para>The train runs at least once, so its junctions must be idempotent; an irreversible step inside it
    /// takes its own claim. An invoked train does not count against the machine's one <c>RunsOnce</c> effect.</para>
    /// </summary>
    /// <typeparam name="TTrain">The train's interface, which is its canonical name.</typeparam>
    /// <typeparam name="TInput">The train's input type.</typeparam>
    /// <typeparam name="TOutput">The train's output type; a successful run's output is the <c>OnDone</c> outcome's input.</typeparam>
    /// <param name="input">
    /// Builds the run's input from the context the state was entered with. It runs on the server only: the IR
    /// does not carry it, and the TypeScript twin never starts a train.
    /// </param>
    IInvokeBuilder<TState, TTrigger> Invokes<TTrain, TInput, TOutput>(
        Func<JsonObject, TInput> input
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput> =>
        throw new NotSupportedException($"{GetType().Name} does not support Invokes.");

    /// <summary>
    /// Start a child machine whenever the machine enters this state, and route how the child ends with the returned
    /// builder's <c>OnDone</c>, <c>OnFailed</c> and <c>OnCancelled</c>, as for an invoked train. The child belongs to
    /// the state: leaving the state removes it, and its end is applied only to the entry that started it. A state
    /// spawns or invokes at most one thing.
    ///
    /// <para>The child starts in its own initial state, owned by whoever owns this instance, and moves through its
    /// own events. It ends when it enters a state declared with <see cref="Ends"/> (this state's <c>OnDone</c>, with
    /// the child's context as the outcome's input) or <see cref="Fails"/> (<c>OnFailed</c>). A child that disappears
    /// before it ends takes <c>OnCancelled</c>.</para>
    /// </summary>
    /// <typeparam name="TChild">The child machine's class.</typeparam>
    /// <typeparam name="TContext">This state's context record, which holds the child's id.</typeparam>
    /// <typeparam name="TOutput">
    /// The fields of the child's context the parent reads when it ends: the <c>OnDone</c> outcome's input. Fields the
    /// child holds beyond these are not passed on.
    /// </typeparam>
    /// <param name="childId">
    /// The field of this state's context the server writes the child's id into when it starts the child, so a client
    /// finds the child. A <c>Guid?</c>; only the server writes it.
    /// </param>
    /// <param name="context">
    /// Builds the child's initial context from the context this state was entered with; null starts it with its
    /// declared one. It runs on the server only.
    /// </param>
    IInvokeBuilder<TState, TTrigger> Spawns<TChild, TContext, TOutput>(
        Expression<Func<TContext, Guid?>> childId,
        Func<JsonObject, JsonObject>? context = null
    )
        where TChild : class =>
        throw new NotSupportedException($"{GetType().Name} does not support Spawns.");

    /// <summary>
    /// Mark this state as where the machine ends when another machine's state spawned it: entering it gives the
    /// parent its <c>OnDone</c> outcome, with this instance's context. A state ends or fails, not both, and invokes or
    /// spawns nothing. On an instance nothing spawned it means nothing.
    /// </summary>
    IStateBuilder<TState, TTrigger> Ends() =>
        throw new NotSupportedException($"{GetType().Name} does not support Ends.");

    /// <summary>
    /// Mark this state as where the machine fails when another machine's state spawned it: entering it gives the
    /// parent its <c>OnFailed</c> outcome. Otherwise as <see cref="Ends"/>.
    /// </summary>
    IStateBuilder<TState, TTrigger> Fails() =>
        throw new NotSupportedException($"{GetType().Name} does not support Fails.");

    /// <summary>
    /// Leave this state once it has been held for <paramref name="delay"/>: the timer starts on every entry (a
    /// self-transition included) and is cancelled on leaving. Finish it with
    /// <see cref="ITimerBuilder{TState,TTrigger}.GoTo"/>. Only the server fires it, when it is due: a client never sends
    /// the timer trigger (<c>&lt;State&gt;.after</c>), and an advance refuses it as <c>TIMER_BOUND</c>. A state has at
    /// most one timer. <see cref="MachineBuilder{TState,TTrigger}.Build"/> refuses a duration that is not positive or
    /// is above <see cref="MachineTimers.MaxDuration"/>, a timer back into its own state, timers that form a cycle no
    /// event breaks, and a timer into an effect's target or an invoked train's outcome target.
    /// </summary>
    /// <param name="delay">How long the state is held before the timer fires.</param>
    ITimerBuilder<TState, TTrigger> After(TimeSpan delay) =>
        throw new NotSupportedException($"{GetType().Name} does not support After.");

    /// <summary>
    /// Leave this state once it has been held for the duration a context field holds, bounded by
    /// <paramref name="min"/> and <paramref name="max"/>. Finish it with
    /// <see cref="ITimerBuilder{TState,TTrigger}.GoTo"/>. The field holds an ISO-8601 duration (<c>"PT5M"</c>) and is
    /// read once, when the state is entered: entering with a field that is missing, not such a duration, or outside the
    /// bounds is refused as <c>INVALID_INPUT</c>. Otherwise as <see cref="After(TimeSpan)"/>.
    /// <see cref="MachineBuilder{TState,TTrigger}.Build"/> refuses bounds that are not positive, a minimum above the
    /// maximum, a maximum above <see cref="MachineTimers.MaxDuration"/>, a field the state's declared context does not
    /// have or does not type as a <see cref="TimeSpan"/>, and a field marked <c>[TraxSensitive]</c> (a deadline is
    /// exported and shown). A selector that is not a single field builds, but the machine cannot be exported to the IR.
    /// </summary>
    /// <typeparam name="TContext">The state's context record.</typeparam>
    /// <param name="duration">The context field holding the duration, e.g. <c>c =&gt; c.Patience</c>.</param>
    /// <param name="min">The shortest duration the field may hold.</param>
    /// <param name="max">The longest duration the field may hold.</param>
    ITimerBuilder<TState, TTrigger> After<TContext>(
        Expression<Func<TContext, TimeSpan?>> duration,
        TimeSpan min,
        TimeSpan max
    ) => throw new NotSupportedException($"{GetType().Name} does not support After.");
}
