using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    IReadOnlyList<EffectBinding<TState, TTrigger>> Effects,
    DeclarativeModel<TState, TTrigger>? Declarative = null
)
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// The engine that interprets <see cref="Definition"/>, created once with the record. Use it to advance,
    /// rehydrate or serialize snapshots in memory (for example in tests) without any persistence.
    /// </summary>
    public SnapshotMachine<TState, TTrigger> Engine { get; } = new(Definition);

    /// <summary>The states that invoke a train, keyed by state. Empty for a machine that invokes nothing.</summary>
    internal IReadOnlyDictionary<TState, InvokeDefinition<TState>> Invokes => Definition.Invokes;

    /// <summary>
    /// The states only the server may put a draft in or take it out of, so a soft autosave may not: committed
    /// states, effect targets, states that invoke a train, and every target an invoked train's outcome reaches.
    /// </summary>
    internal IReadOnlySet<TState> ReservedStates =>
        CommittedStates
            .Concat(Effects.Select(e => e.To))
            .Concat(Invokes.Keys)
            .Concat(Invokes.Values.SelectMany(i => i.Targets))
            .ToHashSet();

    /// <summary>
    /// Whether the machine's instances belong to the system: <c>IMachineInstances.Start</c> creates them, and no
    /// user's draft operation reaches the machine. Declared with <see cref="IMachineBuilder{TState,TTrigger}.SystemOwned"/>.
    /// </summary>
    internal bool SystemOwned { get; init; }

    /// <summary>
    /// The most invoked runs one user may have live in this machine at once; entering an invoking state past it is
    /// refused. Declared with <see cref="IMachineBuilder{TState,TTrigger}.InvokedRunLimit"/>; system owners are not
    /// capped.
    /// </summary>
    internal int InvokedRunLimit { get; init; } = DefaultInvokedRunLimit;

    /// <summary>The per-user live invoked run limit a machine has when it declares none.</summary>
    internal const int DefaultInvokedRunLimit = 10;
}

/// <summary>The root of the fluent configuration. See <see cref="MachineBuilder{TState,TTrigger}"/>.</summary>
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
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Invokes)]
    IMachineBuilder<TState, TTrigger> SystemOwned() =>
        throw new NotSupportedException($"{GetType().Name} does not support SystemOwned.");

    /// <summary>
    /// The most invoked runs one user may have live in this machine at once: entering an invoking state when the
    /// user already holds that many is refused with <c>invoke-limit-reached</c>, and nothing is written. A run is
    /// live from the entry that queued it until its state is left or its outcome is applied. Defaults to 10.
    /// System-owned instances are not capped here; the dispatcher's <c>MaxActiveJobs</c> bounds them.
    /// </summary>
    /// <param name="limit">The limit, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Invokes)]
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
}

/// <summary>
/// Authors a machine's differential fuzzing inputs. Each call adds one representative input. The harness
/// always fires a no-input case per trigger, so an explicit <see cref="EmptySample"/> (<c>{}</c>) is a
/// distinct case. Typed overloads serialize the record with camelCase names (nulls kept), matching the field
/// names the guards and reducers read; raw <see cref="JsonObject"/> overloads give exact control.
/// </summary>
public interface IDifferentialBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>A representative input for <paramref name="trigger"/>, from a typed record.</summary>
    IDifferentialBuilder<TState, TTrigger> Sample<TInput>(TTrigger trigger, TInput input);

    /// <summary>A representative input for <paramref name="trigger"/>, as a raw JSON object.</summary>
    IDifferentialBuilder<TState, TTrigger> Sample(TTrigger trigger, JsonObject input);

    /// <summary>An empty (<c>{}</c>) input for <paramref name="trigger"/> — distinct from the no-input case.</summary>
    IDifferentialBuilder<TState, TTrigger> EmptySample(TTrigger trigger);

    /// <summary>A seed context for <paramref name="state"/> (a BFS start point that reaches states the initial snapshot can't), from a typed record.</summary>
    IDifferentialBuilder<TState, TTrigger> Seed<TContext>(TState state, TContext context);

    /// <summary>A seed context for <paramref name="state"/>, as a raw JSON object.</summary>
    IDifferentialBuilder<TState, TTrigger> Seed(TState state, JsonObject context);

    /// <summary>A dense probe context crossed with EVERY state (exercises guards/validators on unreachable-but-sendable snapshots), from a typed record.</summary>
    IDifferentialBuilder<TState, TTrigger> Probe<TContext>(TContext context);

    /// <summary>A dense probe context, as a raw JSON object.</summary>
    IDifferentialBuilder<TState, TTrigger> Probe(JsonObject context);

    /// <summary>
    /// A representative output of the train <paramref name="invokingState"/> invokes, fired as that state's
    /// <c>done</c> outcome trigger (<c>&lt;State&gt;.done</c>), from a typed record. The harness also fires every
    /// outcome trigger with no input.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Invokes)]
    IDifferentialBuilder<TState, TTrigger> OutcomeSample<TOutput>(
        TState invokingState,
        TOutput output
    ) => throw new NotSupportedException($"{GetType().Name} does not support outcome samples.");
}

/// <summary>Per-state configuration: its context validator, whether it is committed, and its transitions.</summary>
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
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Invokes)]
    IInvokeBuilder<TState, TTrigger> Invokes<TTrain, TInput, TOutput>(
        Func<JsonObject, TInput> input
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput> =>
        throw new NotSupportedException($"{GetType().Name} does not support Invokes.");
}

/// <summary>A single transition: its guard, message, reducer, optional exactly-once effect, and destination.</summary>
public interface ITransitionBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Only take this edge when the predicate holds. Guards for one (state, trigger) must be mutually exclusive.</summary>
    ITransitionBuilder<TState, TTrigger> When(Func<JsonObject, JsonNode?, bool> guard);

    /// <summary>Only take this edge when the declarative <see cref="Rule"/> holds (the exportable, string-free guard).</summary>
    ITransitionBuilder<TState, TTrigger> When(Rule guard);

    /// <summary>Declare this trigger's input shape from a record, so the IR carries a typed input schema for it.</summary>
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
}

/// <summary>
/// The fluent, self-contained way to author a machine (inspired by Stateless's <c>Configure</c>). Every
/// rule, guards, reducers, per-state validators, committed states, and the exactly-once effect, is
/// declared here, on the transition it belongs to, and nothing leaks into the composition root. The result
/// is the same <see cref="MachineDefinition{TState,TTrigger}"/> the engine already interprets.
/// </summary>
public sealed partial class MachineBuilder<TState, TTrigger> : IMachineBuilder<TState, TTrigger>
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
    private readonly Dictionary<TState, ContextSchema> _contextSchemas = [];
    private readonly Dictionary<TTrigger, ContextSchema> _triggerInputs = [];
    private readonly Dictionary<TState, List<Rule>> _stateInvariants = [];
    private readonly List<DeclarativeTransition<TState, TTrigger>> _declarativeTransitions = [];
    private readonly Dictionary<TTrigger, List<JsonNode>> _diffSamples = [];
    private readonly Dictionary<TState, JsonNode> _diffSeeds = [];
    private readonly List<JsonNode> _diffContexts = [];
    private readonly Dictionary<string, Func<JsonObject, JsonNode?, bool>> _customGuards = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, Func<JsonObject, JsonNode?, JsonObject>> _customReducers =
        new(StringComparer.Ordinal);
    private bool _usedDeclarative;
    private bool _systemOwned;
    private int _invokedRunLimit = BuiltMachine<TState, TTrigger>.DefaultInvokedRunLimit;
    private readonly List<InvokeDraft> _invokes = [];
    private readonly Dictionary<TState, List<JsonNode>> _diffOutcomeSamples = [];

    // States whose validator is a Holds delegate (not rebuilt from Context/Requires since), so the IR exporter
    // can refuse a declarative machine that would export them with no validator.
    private readonly HashSet<TState> _delegateValidators = [];

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Id(string id)
    {
        if (id is null || !KebabCaseId().IsMatch(id))
            throw new ArgumentException(
                $"The machine id '{id}' is not kebab-case. Use lowercase letters and digits in hyphen-separated "
                    + "words, starting with a letter, e.g. `.Id(\"checkout\")` or `.Id(\"turnstile-two\")`.",
                nameof(id)
            );
        _id = id;
        return this;
    }

    // The same pattern the `trax machine` CLI enforces on the ids it generates from, so every id a machine can
    // be built with is one the toolchain can emit. `\z`, not `$`: `$` also matches before a final newline.
    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCaseId();

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Version(int version)
    {
        _version = version;
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> StartsAt(TState state, Func<JsonObject> initialContext)
    {
        _initial = state;
        _initialContext = initialContext;
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> MigrateFrom(
        int fromVersion,
        Func<string, JsonObject, MigrationResult> migrate
    )
    {
        _migrations[fromVersion] = migrate;
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Differential(
        Action<IDifferentialBuilder<TState, TTrigger>> configure
    )
    {
        configure(new DifferentialBuilder(this));
        return this;
    }

    /// <inheritdoc/>
    public IStateBuilder<TState, TTrigger> In(TState state) => new StateBuilder(this, state);

#pragma warning disable TRAXEXP002 // The experimental feature's own implementation.
    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> SystemOwned()
    {
        _systemOwned = true;
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> InvokedRunLimit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        _invokedRunLimit = limit;
        return this;
    }
#pragma warning restore TRAXEXP002

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> CustomGuard(
        string name,
        Func<JsonObject, JsonNode?, bool> guard
    )
    {
        _customGuards[name] = guard;
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> CustomReducer(
        string name,
        Func<JsonObject, JsonNode?, JsonObject> reducer
    )
    {
        _customReducers[name] = reducer;
        return this;
    }

    /// <summary>Compile the configuration into an engine-ready definition + host metadata.</summary>
    /// <exception cref="InvalidOperationException">
    /// The machine has no id or no start state, names a custom rule or reduction with no handler bound, binds
    /// more than one effect with <c>RunsOnce</c>, or enters an effect's target state by any other transition. Or
    /// a state invokes more than one train, invokes one by a class rather than its interface, or lacks
    /// <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c> (or declares either of the last two twice); an
    /// outcome goes to an effect's target; or an ordinary transition enters a state an outcome goes to.
    /// </exception>
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

        RefuseUnboundCustomNames();

        // The persistence layer runs one bound effect per machine; a second would be declared and never run.
        if (_effects.Count > 1)
            throw new InvalidOperationException(
                $"The machine '{_id}' binds {_effects.Count} effects with RunsOnce, but a machine runs exactly one "
                    + "irreversible effect. Keep RunsOnce on the one transition that performs it."
            );

        RefuseOtherEdgesIntoEffectTargets();

        var invokes = BuildInvokes();
        RefuseOtherEdgesIntoOutcomeTargets(invokes);

        var definition = new MachineDefinition<TState, TTrigger>
        {
            Id = _id,
            Version = _version,
            InitialState = _initial.Value,
            CreateInitialContext = _initialContext,
            Transitions = _transitions,
            ContextValidators = _validators,
            Migrations = _migrations,
            Invokes = invokes,
        };

        var differential =
            _diffSamples.Count > 0
            || _diffSeeds.Count > 0
            || _diffContexts.Count > 0
            || _diffOutcomeSamples.Count > 0
                ? new DifferentialModel<TState, TTrigger>(
                    _diffSamples.ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyList<JsonNode>)kv.Value
                    ),
                    _diffSeeds,
                    _diffContexts
                )
                {
                    OutcomeSamples = _diffOutcomeSamples.ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyList<JsonNode>)kv.Value
                    ),
                }
                : null;

        if (differential is not null && !_usedDeclarative)
            throw new InvalidOperationException(
                ".Differential(...) is only valid on a declaratively-authored machine (use .Context/.When/.Reduce). "
                    + "The differential inputs are carried through the IR, which a raw-delegate machine cannot export."
            );

        var declarative = _usedDeclarative
            ? new DeclarativeModel<TState, TTrigger>(
                _contextSchemas,
                _triggerInputs,
                _declarativeTransitions,
                _stateInvariants.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Count == 1 ? kv.Value[0] : (Rule)new Rule.All(kv.Value)
                )
            )
            {
                Differential = differential,
                DelegateValidatedStates = _delegateValidators.ToHashSet(),
            }
            : null;

        return new BuiltMachine<TState, TTrigger>(definition, _committed, _effects, declarative)
        {
            SystemOwned = _systemOwned,
            InvokedRunLimit = _invokedRunLimit,
        };
    }

    // An effect's target state means "the effect ran": the draft reaches it with the receipt the effect produced.
    // A second edge into it would let the plain advance put a draft there with no effect run, so the machine is
    // refused. A self-loop on the target does not enter it and is allowed.
    private void RefuseOtherEdgesIntoEffectTargets()
    {
        foreach (var effect in _effects)
        foreach (var t in _transitions)
        {
            if (!EqualityComparer<TState>.Default.Equals(t.To, effect.To))
                continue;
            if (EqualityComparer<TState>.Default.Equals(t.From, effect.To))
                continue;
            if (
                EqualityComparer<TState>.Default.Equals(t.From, effect.From)
                && EqualityComparer<TTrigger>.Default.Equals(t.Trigger, effect.Trigger)
            )
                continue;
            throw new InvalidOperationException(
                $"The machine '{_id}' enters {effect.To} from {t.From} on {t.Trigger}, but {effect.To} is the target "
                    + $"of the effect bound with RunsOnce on {effect.From} -> {effect.Trigger}. Only the effect's own "
                    + "transition may enter the state that records its receipt; route the other edge to another state."
            );
        }
    }

    // Every invoking state says where each outcome goes, and invokes one train. A missing OnCancelled would leave
    // a cancelled run with no declared edge, and an operator's cancel is not "the failure state" once a machine
    // has several; a second Invokes on one state would give one entry two runs and one token.
    private Dictionary<TState, InvokeDefinition<TState>> BuildInvokes()
    {
        var problems = new List<string>();
        foreach (var group in _invokes.GroupBy(i => i.State))
        {
            var drafts = group.ToList();
            if (drafts.Count > 1)
                problems.Add(
                    $"the state {group.Key} invokes {drafts.Count} trains "
                        + $"({string.Join(", ", drafts.Select(d => d.TrainType.Name))}), but a state invokes at "
                        + "most one. Give each train a state of its own."
                );
        }
        foreach (var draft in _invokes)
        {
            var state = draft.State;
            var train = draft.TrainType.Name;
            if (!draft.TrainType.IsInterface)
                problems.Add(
                    $"the state {state} invokes {train}, which is a class. Name the train by its interface "
                        + "(Invokes<IMyTrain, TInput, TOutput>), which is the train's canonical name."
                );
            if (draft.Done.Count == 0)
                problems.Add(
                    $"the state {state} invokes {train} but declares no OnDone. Add `.OnDone({typeof(TState).Name}.X)` "
                        + "for where a successful run goes."
                );
            problems.AddRange(
                ExactlyOnce(draft.Failed.Count, state, train, "OnFailed", "a failed")
            );
            problems.AddRange(
                ExactlyOnce(draft.Cancelled.Count, state, train, "OnCancelled", "a cancelled")
            );
            foreach (var target in draft.Done.Concat(draft.Failed).Concat(draft.Cancelled))
            {
                var effect = _effects.FirstOrDefault(e =>
                    EqualityComparer<TState>.Default.Equals(e.To, target.To)
                );
                if (effect is not null)
                    problems.Add(
                        $"the state {state} sends an outcome of {train} to {target.To}, which is the target of the "
                            + $"effect bound with RunsOnce on {effect.From} -> {effect.Trigger}. Only the effect may "
                            + "enter the state that records its receipt; send the outcome to another state."
                    );
            }
        }

        foreach (
            var state in _diffOutcomeSamples.Keys.Where(s => _invokes.All(i => !i.State.Equals(s)))
        )
            problems.Add(
                $"the differential declares an outcome sample for {state}, which invokes no train. Declare "
                    + "OutcomeSample only for a state with Invokes."
            );

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(
                        " ",
                        problems.Distinct().Select(p => char.ToUpperInvariant(p[0]) + p[1..])
                    )
            );

        return _invokes.ToDictionary(
            d => d.State,
            d => new InvokeDefinition<TState>(
                d.State,
                d.TrainType,
                d.InputType,
                d.OutputType,
                d.CreateInput,
                d.Done,
                d.Failed[0],
                d.Cancelled[0]
            )
        );
    }

    private static IEnumerable<string> ExactlyOnce(
        int count,
        TState state,
        string train,
        string method,
        string run
    )
    {
        if (count == 0)
            yield return $"the state {state} invokes {train} but declares no {method}. Every invoking state says "
                + $"where {run} run goes; add `.{method}({typeof(TState).Name}.X)`.";
        else if (count > 1)
            yield return $"the state {state} declares {method} {count} times. Declare it exactly once.";
    }

    // An outcome target means "the train produced this": the draft reaches it with the context its outcome
    // reduced. An ordinary transition into it would let a client put a draft there with no run, carrying whatever
    // its own input reduced, so the machine is refused, as it is for an effect's target. A self-loop on the
    // target does not enter it and is allowed. So is an edge into a target that itself invokes a train: entering
    // it only queues a new run and forges no result, and it is how a chained stage is retried.
    private void RefuseOtherEdgesIntoOutcomeTargets(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes
    )
    {
        foreach (var invoke in invokes.Values)
        foreach (var target in invoke.Targets)
        foreach (var t in _transitions)
        {
            if (!EqualityComparer<TState>.Default.Equals(t.To, target))
                continue;
            if (EqualityComparer<TState>.Default.Equals(t.From, target))
                continue;
            if (invokes.ContainsKey(target))
                continue;
            throw new InvalidOperationException(
                $"The machine '{_id}' enters {target} from {t.From} on {t.Trigger}, but {target} is where the "
                    + $"outcome of {invoke.TrainType.Name}, invoked in {invoke.State}, goes. Only the outcome may "
                    + "enter it; route the other edge to another state."
            );
        }
    }

    // A custom rule or reduction with no C# handler would compile to a guard that is always false and a
    // reducer that silently keeps the context, while a TypeScript twin given its handler behaves otherwise.
    // Refuse the machine instead. Handlers may be bound before or after the rules that name them.
    private void RefuseUnboundCustomNames()
    {
        var outcomeEdges = _invokes
            .SelectMany(i => i.Done.Concat(i.Failed).Concat(i.Cancelled))
            .ToList();
        var rules = _declarativeTransitions
            .Select(t => t.Guard)
            .Concat(_stateInvariants.Values.SelectMany(list => list))
            .Concat(outcomeEdges.Select(e => e.Guard))
            .OfType<Rule>();
        foreach (var name in rules.SelectMany(CustomNames).Distinct(StringComparer.Ordinal))
            if (!_customGuards.ContainsKey(name))
                throw new InvalidOperationException(
                    $"The machine uses the custom rule '{name}', but no C# handler is bound to it. "
                        + $"Add `.CustomGuard(\"{name}\", (context, input) => ...)` in Configure."
                );

        foreach (
            var name in _declarativeTransitions
                .Select(t => t.Reduce)
                .Concat(outcomeEdges.Select(e => e.Reduce))
                .OfType<Reduction.Custom>()
                .Select(c => c.Name)
                .Distinct(StringComparer.Ordinal)
        )
            if (!_customReducers.ContainsKey(name))
                throw new InvalidOperationException(
                    $"The machine uses the custom reduction '{name}', but no C# handler is bound to it. "
                        + $"Add `.CustomReducer(\"{name}\", (context, input) => ...)` in Configure."
                );
    }

    private static IEnumerable<string> CustomNames(Rule rule) =>
        rule switch
        {
            Rule.Custom c => [c.Name],
            Rule.All all => all.Rules.SelectMany(CustomNames),
            Rule.Any any => any.Rules.SelectMany(CustomNames),
            _ => [],
        };

    private sealed class StateBuilder(MachineBuilder<TState, TTrigger> owner, TState state)
        : IStateBuilder<TState, TTrigger>
    {
        public IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator)
        {
            owner._validators[state] = validator;
            owner._delegateValidators.Add(state);
            return this;
        }

        public IStateBuilder<TState, TTrigger> Context<TContext>() =>
            SetSchema(SchemaReflection.For<TContext>());

        public IStateBuilder<TState, TTrigger> Context() => SetSchema(ContextSchema.Empty);

        private IStateBuilder<TState, TTrigger> SetSchema(ContextSchema schema)
        {
            owner._usedDeclarative = true;
            owner._contextSchemas[state] = schema;
            RebuildValidator();
            return this;
        }

        public IStateBuilder<TState, TTrigger> Requires(Rule constraint)
        {
            owner._usedDeclarative = true;
            if (!owner._stateInvariants.TryGetValue(state, out var list))
                owner._stateInvariants[state] = list = [];
            list.Add(constraint);
            RebuildValidator();
            return this;
        }

        // The state's validator is its schema (if declared) AND each requirement, composed. Rebuilt whenever
        // Context or Requires changes, so the order of those calls does not matter.
        private void RebuildValidator()
        {
            var schema = owner._contextSchemas.GetValueOrDefault(state);
            var invariants = owner._stateInvariants.GetValueOrDefault(state);
            owner._delegateValidators.Remove(state);
            owner._validators[state] = ctx =>
            {
                if (schema is not null)
                {
                    var error = SchemaValidator.Validate(schema, ctx);
                    if (error is not null)
                        return error;
                }
                if (invariants is not null)
                    foreach (var rule in invariants)
                        if (!RuleEvaluator.Evaluate(rule, ctx, input: null, owner._customGuards))
                            return "A state requirement was not satisfied.";
                return null;
            };
        }

        public IStateBuilder<TState, TTrigger> Committed()
        {
            owner._committed.Add(state);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> On(TTrigger trigger) =>
            new TransitionBuilder(owner, this, state, trigger);

#pragma warning disable TRAXEXP002 // The experimental feature's own implementation.
        public IInvokeBuilder<TState, TTrigger> Invokes<TTrain, TInput, TOutput>(
            Func<JsonObject, TInput> input
        )
            where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput>
        {
            ArgumentNullException.ThrowIfNull(input);
            var draft = new InvokeDraft(
                state,
                typeof(TTrain),
                typeof(TInput),
                typeof(TOutput),
                context => input(context)
            );
            owner._invokes.Add(draft);
            return new InvokeBuilder(owner, this, draft);
        }
#pragma warning restore TRAXEXP002
    }

    // One Invokes as declared, before Build checks it.
    private sealed record InvokeDraft(
        TState State,
        Type TrainType,
        Type InputType,
        Type OutputType,
        Func<JsonObject, object?> CreateInput
    )
    {
        public List<OutcomeEdge<TState>> Done { get; } = [];
        public List<OutcomeEdge<TState>> Failed { get; } = [];
        public List<OutcomeEdge<TState>> Cancelled { get; } = [];
    }

#pragma warning disable TRAXEXP002 // The experimental feature's own implementation.
    private sealed class InvokeBuilder(
        MachineBuilder<TState, TTrigger> owner,
        IStateBuilder<TState, TTrigger> state,
        InvokeDraft draft
    ) : IInvokeBuilder<TState, TTrigger>
    {
        public IInvokeBuilder<TState, TTrigger> OnDone(
            TState target,
            Rule? when = null,
            Reduction? reduce = null
        )
        {
            draft.Done.Add(Edge(target, when, reduce));
            return this;
        }

        public IInvokeBuilder<TState, TTrigger> OnFailed(TState target, Reduction? reduce = null)
        {
            draft.Failed.Add(Edge(target, null, reduce));
            return this;
        }

        public IInvokeBuilder<TState, TTrigger> OnCancelled(TState target, Reduction? reduce = null)
        {
            draft.Cancelled.Add(Edge(target, null, reduce));
            return this;
        }

        // Compiled the way a transition's declarative When/Reduce are, so an outcome edge runs the same rule and
        // reduction interpreters as every other edge, and exports as the same data.
        private OutcomeEdge<TState> Edge(TState target, Rule? when, Reduction? reduce) =>
            new(
                target,
                when,
                reduce,
                when is null
                    ? null
                    : (ctx, input) => RuleEvaluator.Evaluate(when, ctx, input, owner._customGuards),
                reduce is null
                    ? null
                    : (ctx, input) =>
                        ReductionEvaluator.Apply(
                            reduce,
                            ctx,
                            input,
                            owner._initialContext?.Invoke() ?? new JsonObject(),
                            owner._customReducers
                        )
            );

        public IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator) =>
            state.Holds(validator);

        public IStateBuilder<TState, TTrigger> Context<TContext>() => state.Context<TContext>();

        public IStateBuilder<TState, TTrigger> Context() => state.Context();

        public IStateBuilder<TState, TTrigger> Requires(Rule constraint) =>
            state.Requires(constraint);

        public IStateBuilder<TState, TTrigger> Committed() => state.Committed();

        public ITransitionBuilder<TState, TTrigger> On(TTrigger trigger) => state.On(trigger);

        public IInvokeBuilder<TState, TTrigger> Invokes<TTrain, TInput, TOutput>(
            Func<JsonObject, TInput> input
        )
            where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput> =>
            state.Invokes<TTrain, TInput, TOutput>(input);
    }
#pragma warning restore TRAXEXP002

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
        private Rule? _guardRule;
        private Reduction? _reduction;

        public ITransitionBuilder<TState, TTrigger> When(Func<JsonObject, JsonNode?, bool> guard)
        {
            // The edge's guard is now a delegate; drop any rule recorded earlier so the declarative model
            // does not export a guard the engine no longer runs.
            _guard = guard;
            _guardRule = null;
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> When(Rule guard)
        {
            owner._usedDeclarative = true;
            _guardRule = guard;
            // Compile the rule down to the delegate the engine already runs; the engine is untouched.
            _guard = (ctx, input) => RuleEvaluator.Evaluate(guard, ctx, input, owner._customGuards);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> WithInput<TInput>()
        {
            owner._usedDeclarative = true;
            owner._triggerInputs[trigger] = SchemaReflection.For<TInput>();
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
            _reduction = null;
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> Reduce(Reduction reduce)
        {
            owner._usedDeclarative = true;
            _reduction = reduce;
            _reduce = (ctx, input) =>
                ReductionEvaluator.Apply(
                    reduce,
                    ctx,
                    input,
                    owner._initialContext?.Invoke() ?? new JsonObject(),
                    owner._customReducers
                );
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
            owner._declarativeTransitions.Add(
                new DeclarativeTransition<TState, TTrigger>(
                    from,
                    trigger,
                    target,
                    _guardRule,
                    _reduction
                )
            );
            return state;
        }
    }

    private sealed class DifferentialBuilder(MachineBuilder<TState, TTrigger> owner)
        : IDifferentialBuilder<TState, TTrigger>
    {
        // camelCase to match the field names the guards/reducers read (SchemaReflection uses the same policy);
        // nulls kept so a nullable field's null appears in a probe context exactly as the wire carries it. An
        // empty input uses EmptySample, so all-null typed inputs are never how {} is authored.
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public IDifferentialBuilder<TState, TTrigger> Sample<TInput>(
            TTrigger trigger,
            TInput input
        ) => Sample(trigger, ToObject(input));

        public IDifferentialBuilder<TState, TTrigger> Sample(TTrigger trigger, JsonObject input)
        {
            if (!owner._diffSamples.TryGetValue(trigger, out var list))
                owner._diffSamples[trigger] = list = [];
            list.Add(input);
            return this;
        }

        public IDifferentialBuilder<TState, TTrigger> EmptySample(TTrigger trigger) =>
            Sample(trigger, new JsonObject());

        public IDifferentialBuilder<TState, TTrigger> Seed<TContext>(
            TState state,
            TContext context
        ) => Seed(state, ToObject(context));

        public IDifferentialBuilder<TState, TTrigger> Seed(TState state, JsonObject context)
        {
            owner._diffSeeds[state] = context;
            return this;
        }

        public IDifferentialBuilder<TState, TTrigger> Probe<TContext>(TContext context) =>
            Probe(ToObject(context));

        public IDifferentialBuilder<TState, TTrigger> Probe(JsonObject context)
        {
            owner._diffContexts.Add(context);
            return this;
        }

        public IDifferentialBuilder<TState, TTrigger> OutcomeSample<TOutput>(
            TState invokingState,
            TOutput output
        )
        {
            if (!owner._diffOutcomeSamples.TryGetValue(invokingState, out var list))
                owner._diffOutcomeSamples[invokingState] = list = [];
            list.Add(ToObject(output));
            return this;
        }

        private static JsonObject ToObject<T>(T value) =>
            JsonSerializer.SerializeToNode(value, Json) as JsonObject
            ?? throw new InvalidOperationException(
                $"A differential input must serialize to a JSON object, but {typeof(T).Name} did not."
            );
    }
}
