using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>The root of the fluent configuration. See <see cref="MachineBuilder{TState,TTrigger}"/>.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#imachinebuilder">SDK reference</seealso>
public interface IMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// The machine's stable id, written into every snapshot. Required. It must be kebab-case: lowercase letters
    /// and digits in hyphen-separated words, starting with a letter (<c>checkout</c>, <c>turnstile-two</c>),
    /// because it becomes a file name, a module name and a key segment in every generated artifact.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or not kebab-case.</exception>
    IMachineBuilder<TState, TTrigger> Id(string id);

    /// <summary>The definition version (drives migration). Defaults to 1.</summary>
    IMachineBuilder<TState, TTrigger> Version(int version);

    /// <summary>The initial state and its fresh context. Required.</summary>
    IMachineBuilder<TState, TTrigger> StartsAt(TState state, Func<JsonObject> initialContext);

    /// <summary>Register a forward migration from <paramref name="fromVersion"/> to the next version.</summary>
    IMachineBuilder<TState, TTrigger> MigrateFrom(
        int fromVersion,
        Func<string, JsonObject, MigrationResult> migrate
    );

    /// <summary>
    /// Author the differential fuzzing inputs (test-only) the cross-language differential harness enumerates:
    /// representative per-trigger input samples, per-state seed contexts, and dense probe contexts. Declared
    /// here so the C# machine is the single source; the IR exporter carries them and the harness enumerates
    /// off the IR, with no hand-written machine.json. Only valid on a declaratively-authored machine (it rides
    /// the IR). Omit for machines with no cross-language differential.
    /// </summary>
    IMachineBuilder<TState, TTrigger> Differential(
        Action<IDifferentialBuilder<TState, TTrigger>> configure
    );

    /// <summary>Begin configuring transitions and rules for a state.</summary>
    IStateBuilder<TState, TTrigger> In(TState state);

    /// <summary>
    /// Declare that the machine's instances belong to the system rather than to users. Only
    /// <c>IMachineInstances.Start</c> creates one, from a train or at startup; no user's draft operation (load,
    /// save, advance, send) reaches the machine, which answers them as an unknown machine. A train a system-owned
    /// machine invokes runs under Trax's trusted execution scope, so the host refuses one that declares
    /// <c>[TraxAuthorize]</c>. A machine without this is user-owned, and <c>Start</c> refuses it.
    /// </summary>
    IMachineBuilder<TState, TTrigger> SystemOwned() =>
        throw new NotSupportedException($"{GetType().Name} does not support SystemOwned.");

    /// <summary>
    /// The most live invoked runs one user may hold for entering one of this machine's invoking states to be allowed:
    /// the user's runs are counted across every machine, and entering when they already hold that many is refused
    /// with <c>INVOKE_LIMIT_REACHED</c>, and nothing is written. A run is live from the entry that queued it until it
    /// ends; a dispatched run whose state was left runs on to its next junction, and counts until then. Defaults to
    /// 10. The most one user can hold is the largest limit among the host's machines. System-owned instances are
    /// not capped here; the dispatcher's <c>MaxActiveJobs</c> bounds them.
    /// </summary>
    /// <param name="limit">The limit, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    IMachineBuilder<TState, TTrigger> InvokedRunLimit(int limit) =>
        throw new NotSupportedException($"{GetType().Name} does not support InvokedRunLimit.");

    /// <summary>
    /// Bind the C# handler for a <see cref="Rule.Custom"/> named <paramref name="name"/>, wherever the
    /// machine uses it (a transition's <c>When</c> or a state's <c>Requires</c>). A custom rule is the
    /// escape hatch for a predicate the declarative rules cannot express, hand-written once per runtime:
    /// this is the C# one, and the TypeScript twin's <c>customGuards</c> is the other. Build refuses a
    /// machine that names a custom rule with no handler bound.
    /// </summary>
    IMachineBuilder<TState, TTrigger> CustomGuard(
        string name,
        Func<JsonObject, JsonNode?, bool> guard
    ) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support custom guard handlers."
        );

    /// <summary>
    /// Bind the C# handler for a <see cref="Reduction.Custom"/> named <paramref name="name"/>: it receives
    /// the current context and the trigger input and returns the destination context. The TypeScript
    /// twin's <c>customReducers</c> is the other half. Build refuses a machine that names a custom
    /// reduction with no handler bound.
    /// </summary>
    IMachineBuilder<TState, TTrigger> CustomReducer(
        string name,
        Func<JsonObject, JsonNode?, JsonObject> reducer
    ) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support custom reducer handlers."
        );

    /// <summary>
    /// Declare the machine's <c>start</c>: the record a client starts an instance with, how it becomes the
    /// initial context, and who may start one. A machine a client can reach must declare its start's rule, or the
    /// host refuses to start; <c>Anonymous()</c> on a user-owned machine is refused the same way.
    /// <c>ISnapshotDraftService.Start</c> validates the input against the record's schema (never the context type),
    /// checks the start's rule, applies the reduction to the initial context and mints the instance's id. A host
    /// refuses to start when the reduction of a machine a client can reach copies the input into a field an invoked
    /// train's outcome, the effect or an event gated by <c>Requires</c> or <c>RequiresPolicy</c> writes.
    /// </summary>
    /// <typeparam name="TStartInput">The record a client starts an instance with; never the context type.</typeparam>
    /// <param name="configure">Declares the start's reduction and its rule.</param>
    IMachineBuilder<TState, TTrigger> Start<TStartInput>(
        Action<IStartBuilder<TStartInput>> configure
    ) => throw new NotSupportedException($"{GetType().Name} does not support Start.");

    /// <summary>
    /// Declare who may read an instance through <c>get</c>. A machine a client can reach must declare it, owner-only
    /// being the floor: <c>AnyAuthenticated()</c> or <c>Anonymous()</c> refuses startup, and <c>Requires</c> or
    /// <c>RequiresPolicy</c> narrow the owner's read further.
    /// </summary>
    /// <param name="rule">Declares the read's rule.</param>
    IMachineBuilder<TState, TTrigger> Get(Action<IReadRuleBuilder> rule) =>
        throw new NotSupportedException($"{GetType().Name} does not support Get.");

    /// <summary>
    /// Declare who may watch an instance through the push (<c>&lt;machine&gt;Changed</c>), under the same floor as
    /// <see cref="Get"/>.
    /// </summary>
    /// <param name="rule">Declares the subscription's rule.</param>
    IMachineBuilder<TState, TTrigger> Watch(Action<IReadRuleBuilder> rule) =>
        throw new NotSupportedException($"{GetType().Name} does not support Watch.");

    /// <summary>
    /// Declare who may delete an instance through <c>discard</c>, under the same floor as <see cref="Get"/>:
    /// owner-only, narrowed further by <c>Requires</c> or <c>RequiresPolicy</c>. A machine a client can reach must
    /// declare it. Whatever the rule, an instance in a committed state, an effect's target, a state that invokes a
    /// train, a state an invoked train's outcome reaches or the target of an event gated by a role or a policy is not
    /// discarded, nor is one an effect is running on (<c>DISCARD_NOT_ALLOWED</c>).
    /// </summary>
    /// <param name="rule">Declares the discard's rule.</param>
    IMachineBuilder<TState, TTrigger> Discard(Action<IReadRuleBuilder> rule) =>
        throw new NotSupportedException($"{GetType().Name} does not support Discard.");
}
