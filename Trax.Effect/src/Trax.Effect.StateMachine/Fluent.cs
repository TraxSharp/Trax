using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// One irreversible effect bound to a transition, declared inline on the machine (never wired in DI by
/// hand). The persistence layer resolves <see cref="EffectType"/> from the container and runs it
/// exactly-once when the transition fires.
/// </summary>
public sealed record EffectBinding<TState, TTrigger>(
    TState From,
    TTrigger Trigger,
    TState To,
    Type EffectType,
    string KeyPrefix
)
    where TState : struct, Enum
    where TTrigger : struct, Enum;

/// <summary>
/// The compiled result of <see cref="MachineBuilder{TState,TTrigger}.Build"/>: the engine-ready
/// <see cref="MachineDefinition{TState,TTrigger}"/> plus the metadata a host needs (committed states and
/// the exactly-once effect bindings). This keeps the fluent authoring surface separate from the engine.
/// </summary>
public sealed record BuiltMachine<TState, TTrigger>(
    MachineDefinition<TState, TTrigger> Definition,
    IReadOnlyCollection<TState> CommittedStates,
    IReadOnlyList<EffectBinding<TState, TTrigger>> Effects
)
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public SnapshotMachine<TState, TTrigger> Engine { get; } = new(Definition);
}

/// <summary>The root of the fluent configuration. See <see cref="MachineBuilder{TState,TTrigger}"/>.</summary>
public interface IMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>The machine's stable id, written into every snapshot. Required.</summary>
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

    /// <summary>Begin configuring transitions and rules for a state.</summary>
    IStateBuilder<TState, TTrigger> In(TState state);
}

/// <summary>Per-state configuration: its context validator, whether it is committed, and its transitions.</summary>
public interface IStateBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>The context validator for this state (returns null when valid, else a message). Makes illegal states unrepresentable.</summary>
    IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator);

    /// <summary>Mark this state committed: a soft autosave may not move a draft out of it (the guarded path).</summary>
    IStateBuilder<TState, TTrigger> Committed();

    /// <summary>Begin a transition out of this state on a trigger.</summary>
    ITransitionBuilder<TState, TTrigger> On(TTrigger trigger);
}

/// <summary>A single transition: its guard, message, reducer, optional exactly-once effect, and destination.</summary>
public interface ITransitionBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Only take this edge when the predicate holds. Guards for one (state, trigger) must be mutually exclusive.</summary>
    ITransitionBuilder<TState, TTrigger> When(Func<JsonObject, JsonNode?, bool> guard);

    /// <summary>A human-readable reason surfaced when the guard declines (non-contract detail text).</summary>
    ITransitionBuilder<TState, TTrigger> Because(string guardMessage);

    /// <summary>Produce the destination state's context. Omit to carry the current context forward unchanged.</summary>
    ITransitionBuilder<TState, TTrigger> Reduce(Func<JsonObject, JsonNode?, JsonObject> reduce);

    /// <summary>
    /// Bind the one irreversible effect to this transition. It runs exactly-once (claim before the effect,
    /// lease + fence, crash-retry replays) and its receipt is available to the reducer as
    /// <c>input["receipt"]</c>. The effect implementation is resolved from DI by <typeparamref name="TEffect"/>.
    /// </summary>
    ITransitionBuilder<TState, TTrigger> RunsOnce<TEffect>(string? keyPrefix = null);

    /// <summary>Finish the transition: land in <paramref name="target"/>. Returns the state builder for more transitions.</summary>
    IStateBuilder<TState, TTrigger> To(TState target);
}

/// <summary>
/// The fluent, self-contained way to author a machine (inspired by Stateless's <c>Configure</c>). Every
/// rule, guards, reducers, per-state validators, committed states, and the exactly-once effect, is
/// declared here, on the transition it belongs to, and nothing leaks into the composition root. The result
/// is the same <see cref="MachineDefinition{TState,TTrigger}"/> the engine already interprets.
/// </summary>
public sealed class MachineBuilder<TState, TTrigger> : IMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private string? _id;
    private int _version = 1;
    private TState? _initial;
    private Func<JsonObject>? _initialContext;
    private readonly List<TransitionDefinition<TState, TTrigger>> _transitions = [];
    private readonly Dictionary<TState, Func<JsonObject, string?>> _validators = [];
    private readonly HashSet<TState> _committed = [];
    private readonly List<EffectBinding<TState, TTrigger>> _effects = [];
    private readonly Dictionary<int, Func<string, JsonObject, MigrationResult>> _migrations = [];

    public IMachineBuilder<TState, TTrigger> Id(string id)
    {
        _id = id;
        return this;
    }

    public IMachineBuilder<TState, TTrigger> Version(int version)
    {
        _version = version;
        return this;
    }

    public IMachineBuilder<TState, TTrigger> StartsAt(TState state, Func<JsonObject> initialContext)
    {
        _initial = state;
        _initialContext = initialContext;
        return this;
    }

    public IMachineBuilder<TState, TTrigger> MigrateFrom(
        int fromVersion,
        Func<string, JsonObject, MigrationResult> migrate
    )
    {
        _migrations[fromVersion] = migrate;
        return this;
    }

    public IStateBuilder<TState, TTrigger> In(TState state) => new StateBuilder(this, state);

    /// <summary>Compile the configuration into an engine-ready definition + host metadata.</summary>
    public BuiltMachine<TState, TTrigger> Build()
    {
        if (_id is null)
            throw new InvalidOperationException(
                "A machine needs an Id(...). Add `.Id(\"my-machine\")` in Configure."
            );
        if (_initial is null || _initialContext is null)
            throw new InvalidOperationException(
                "A machine needs a StartsAt(state, () => context). Add `.StartsAt(State.X, () => new JsonObject())` in Configure."
            );

        var definition = new MachineDefinition<TState, TTrigger>
        {
            Id = _id,
            Version = _version,
            InitialState = _initial.Value,
            CreateInitialContext = _initialContext,
            Transitions = _transitions,
            ContextValidators = _validators,
            Migrations = _migrations,
        };

        return new BuiltMachine<TState, TTrigger>(definition, _committed, _effects);
    }

    private sealed class StateBuilder(MachineBuilder<TState, TTrigger> owner, TState state)
        : IStateBuilder<TState, TTrigger>
    {
        public IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator)
        {
            owner._validators[state] = validator;
            return this;
        }

        public IStateBuilder<TState, TTrigger> Committed()
        {
            owner._committed.Add(state);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> On(TTrigger trigger) =>
            new TransitionBuilder(owner, this, state, trigger);
    }

    private sealed class TransitionBuilder(
        MachineBuilder<TState, TTrigger> owner,
        IStateBuilder<TState, TTrigger> state,
        TState from,
        TTrigger trigger
    ) : ITransitionBuilder<TState, TTrigger>
    {
        private Func<JsonObject, JsonNode?, bool>? _guard;
        private string? _guardMessage;
        private Func<JsonObject, JsonNode?, JsonObject>? _reduce;
        private Type? _effectType;
        private string? _effectKeyPrefix;

        public ITransitionBuilder<TState, TTrigger> When(Func<JsonObject, JsonNode?, bool> guard)
        {
            _guard = guard;
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> Because(string guardMessage)
        {
            _guardMessage = guardMessage;
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> Reduce(
            Func<JsonObject, JsonNode?, JsonObject> reduce
        )
        {
            _reduce = reduce;
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> RunsOnce<TEffect>(string? keyPrefix = null)
        {
            _effectType = typeof(TEffect);
            _effectKeyPrefix = keyPrefix;
            return this;
        }

        public IStateBuilder<TState, TTrigger> To(TState target)
        {
            owner._transitions.Add(
                new TransitionDefinition<TState, TTrigger>
                {
                    From = from,
                    Trigger = trigger,
                    To = target,
                    Guard = _guard,
                    GuardMessage = _guardMessage,
                    Reduce = _reduce,
                }
            );
            if (_effectType is not null)
                owner._effects.Add(
                    new EffectBinding<TState, TTrigger>(
                        from,
                        trigger,
                        target,
                        _effectType,
                        _effectKeyPrefix ?? $"{owner._id}:{trigger}"
                    )
                );
            return state;
        }
    }
}
