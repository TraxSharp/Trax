using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trax.Core.Extensions;

namespace Trax.StateMachine;

/// <summary>
/// The fluent, self-contained way to author a machine (inspired by Stateless's <c>Configure</c>). Every
/// rule, guards, reducers, per-state validators, committed states, and the exactly-once effect, is
/// declared here, on the transition it belongs to, and nothing leaks into the composition root. The result
/// is the same <see cref="MachineDefinition{TState,TTrigger}"/> the engine already interprets.
/// </summary>
public sealed partial class MachineBuilder<TState, TTrigger>
    : IMachineBuilder<TState, TTrigger>,
        IIrMachineBuilder<TState, TTrigger>
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
    private readonly Dictionary<TState, Type> _contextTypes = [];
    private readonly List<EdgeInput> _edgeInputs = [];
    private readonly Dictionary<TState, List<Rule>> _stateInvariants = [];
    private readonly List<DeclarativeTransition<TState, TTrigger>> _declarativeTransitions = [];
    private readonly Dictionary<TTrigger, List<JsonNode>> _diffSamples = [];
    private readonly Dictionary<TState, List<JsonNode>> _diffSeeds = [];

    // States seeded twice with an equal context; Build refuses them, since the copy adds nothing to the corpus.
    private readonly HashSet<TState> _diffSeedDuplicates = [];
    private readonly List<JsonNode> _diffContexts = [];
    private readonly Dictionary<string, Func<JsonObject, JsonNode?, bool>> _customGuards = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, Func<JsonObject, JsonNode?, JsonObject>> _customReducers =
        new(StringComparer.Ordinal);
    private bool _usedDeclarative;
    private bool _systemOwned;
    private readonly HashSet<TTrigger> _serverOnly = [];

    void IIrMachineBuilder<TState, TTrigger>.ServerOnly(TTrigger trigger) =>
        _serverOnly.Add(trigger);

    private StartDeclaration? _start;
    private readonly List<ServerWrite> _serverWrites = [];
    private readonly List<EventWrite> _eventWrites = [];
    private int _invokedRunLimit = BuiltMachine<TState, TTrigger>.DefaultInvokedRunLimit;
    private readonly List<InvokeDraft> _invokes = [];
    private readonly Dictionary<TState, InvokeOutcomeKind> _ends = [];
    private readonly HashSet<TState> _endConflicts = [];
    private readonly List<TimerDraft> _timers = [];
    private readonly Dictionary<TState, List<JsonNode>> _diffOutcomeSamples = [];
    private readonly List<EdgeRule> _edgeRules = [];
    private CallerRuleDraft? _startRule;
    private CallerRuleDraft? _getRule;
    private CallerRuleDraft? _watchRule;
    private CallerRuleDraft? _discardRule;

    // States whose validator is a Holds delegate (not rebuilt from Context/Requires since), so the IR exporter
    // can refuse a declarative machine that would export them with no validator.
    private readonly HashSet<TState> _delegateValidators = [];

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Id(string id)
    {
        if (id is null || !KebabCaseId.IsMatch(id))
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
    [GeneratedRegex(
        @"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex KebabCaseId { get; }

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

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Start<TStartInput>(
        Action<IStartBuilder<TStartInput>> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        _startRule = new CallerRuleDraft($"The machine '{_id}' start", read: false);
        var builder = new StartBuilder<TStartInput>(_startRule);
        configure(builder);
        var start = builder.Build();
        // Compiled like a transition's reducer: the custom handlers and the initial context are read when it runs,
        // so their order in Configure does not matter.
        _start = start with
        {
            Apply = (ctx, input) =>
                ReductionEvaluator.Apply(
                    start.Reduction,
                    ctx,
                    input,
                    _initialContext?.Invoke() ?? new JsonObject(),
                    _customReducers
                ),
        };
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Get(Action<IReadRuleBuilder> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _getRule = new CallerRuleDraft($"The machine '{_id}' get", read: true);
        rule(new ReadRuleBuilder(_getRule));
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Watch(Action<IReadRuleBuilder> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _watchRule = new CallerRuleDraft($"The machine '{_id}' push (changed)", read: true);
        rule(new ReadRuleBuilder(_watchRule));
        return this;
    }

    /// <inheritdoc/>
    public IMachineBuilder<TState, TTrigger> Discard(Action<IReadRuleBuilder> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _discardRule = new CallerRuleDraft($"The machine '{_id}' discard", read: true);
        rule(new ReadRuleBuilder(_discardRule));
        return this;
    }

    // Records the start's input record and its reduction, and its rule in the machine's rules.
    private sealed class StartBuilder<TStartInput>(CallerRuleDraft rule)
        : IStartBuilder<TStartInput>
    {
        // Reflected at once, so a start record with no finite schema refuses where it is declared.
        private readonly ContextSchema _schema = SchemaReflection.ForInput(typeof(TStartInput));
        private Reduction _reduction = new Reduction.Keep();

        public IStartBuilder<TStartInput> Reduce(Reduction reduce)
        {
            ArgumentNullException.ThrowIfNull(reduce);
            _reduction = reduce;
            return this;
        }

        public IStartBuilder<TStartInput> Requires(params string[] roles)
        {
            rule.Requires(roles);
            return this;
        }

        public IStartBuilder<TStartInput> RequiresPolicy(string policy)
        {
            rule.RequiresPolicy(policy);
            return this;
        }

        public IStartBuilder<TStartInput> AnyAuthenticated()
        {
            rule.Kind(CallerRuleKind.AnyAuthenticated);
            return this;
        }

        public IStartBuilder<TStartInput> Anonymous()
        {
            rule.Kind(CallerRuleKind.Anonymous);
            return this;
        }

        public StartDeclaration Build() => new(typeof(TStartInput), _schema, _reduction);
    }

    // Records a read's rule.
    private sealed class ReadRuleBuilder(CallerRuleDraft rule) : IReadRuleBuilder
    {
        public IReadRuleBuilder OwnerOnly()
        {
            rule.Kind(CallerRuleKind.OwnerOnly);
            return this;
        }

        public IReadRuleBuilder Requires(params string[] roles)
        {
            rule.Requires(roles);
            return this;
        }

        public IReadRuleBuilder RequiresPolicy(string policy)
        {
            rule.RequiresPolicy(policy);
            return this;
        }

        public IReadRuleBuilder AnyAuthenticated()
        {
            rule.Kind(CallerRuleKind.AnyAuthenticated);
            return this;
        }

        public IReadRuleBuilder Anonymous()
        {
            rule.Kind(CallerRuleKind.Anonymous);
            return this;
        }
    }

    // One After as declared, before Build checks it. Its target is set by GoTo.
    private sealed class TimerDraft(
        TState state,
        TimeSpan? fixedDuration,
        LambdaExpression? duration,
        TimeSpan min,
        TimeSpan max
    )
    {
        public TState State { get; } = state;
        public TimeSpan? Fixed { get; } = fixedDuration;
        public LambdaExpression? Duration { get; } = duration;
        public TimeSpan Min { get; } = min;
        public TimeSpan Max { get; } = max;
        public TState? Target { get; set; }
    }

    // Records the timer's target, and the chain continues on the state.
    private sealed class TimerBuilder(IStateBuilder<TState, TTrigger> state, TimerDraft draft)
        : ITimerBuilder<TState, TTrigger>
    {
        public IStateBuilder<TState, TTrigger> GoTo(TState target)
        {
            draft.Target = target;
            return state;
        }
    }

    /// <summary>Compile the configuration into an engine-ready definition + host metadata.</summary>
    /// <exception cref="InvalidOperationException">
    /// The machine has no id or no start state, names a custom rule or reduction with no handler bound, binds
    /// more than one effect with <c>RunsOnce</c>, sends an effect's trigger on any other transition, enters an effect's
    /// target state by any other transition, or binds an
    /// effect that runs from or lands in a state that invokes a train. Or
    /// a state invokes more than one train, invokes one by a class rather than its interface, or lacks
    /// <c>OnDone</c>, <c>OnFailed</c> or <c>OnCancelled</c> (or declares either of the last two twice); an
    /// outcome goes to an effect's target or to the start state (unless that state invokes a train itself); or an
    /// ordinary transition enters a state an outcome goes to. Or an invoked train's output type has no schema to
    /// check it against, or one trigger declares different input records (or a record on one edge and none on
    /// another).
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

        RefuseOtherEdgesOnEffectTriggers();
        RefuseOtherEdgesIntoEffectTargets();
        RefuseEffectsOnInvokingStates();

        var invokes = BuildInvokes();
        RefuseConflictingEnds(invokes);
        RefuseOtherEdgesIntoOutcomeTargets(invokes);
        var timers = BuildTimers(invokes);

        var declaredInputs = DeclaredInputs();
        var inputSchemas = InputSchemas(declaredInputs);
        RefuseUnreadableComparisons(invokes, inputSchemas);
        var rules = BuildRules();

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
            InputSchemas = inputSchemas,
            Timers = timers,
            Ends = new Dictionary<TState, InvokeOutcomeKind>(_ends),
        };

        var declarative = BuildDeclarative(declaredInputs);
        var serverWrites = CollectServerWrites(invokes);
        RefuseOpenWritesAndServerOnlyRules(rules, serverWrites);

        return BuildMachine(definition, declarative, declaredInputs, rules, serverWrites);
    }

    // The declarative model, with the differential inputs it carries; null for a raw-delegate machine.
    private DeclarativeModel<TState, TTrigger>? BuildDeclarative(
        Dictionary<TTrigger, (Type Type, ContextSchema Schema)> declaredInputs
    )
    {
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
                    _diffSeeds.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<JsonNode>)kv.Value),
                    _diffContexts
                )
                {
                    OutcomeSamples = _diffOutcomeSamples.ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyList<JsonNode>)kv.Value
                    ),
                }
                : null;

        if (_diffSeedDuplicates.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' seeds {string.Join(", ", _diffSeedDuplicates)} with the same context more "
                    + "than once in .Differential(...). An equal seed adds nothing to the corpus; remove the copy."
            );

        if (differential is not null && !_usedDeclarative)
            throw new InvalidOperationException(
                ".Differential(...) is only valid on a declaratively-authored machine (use .Context/.When/.Reduce). "
                    + "The differential inputs are carried through the IR, which a raw-delegate machine cannot export."
            );

        var declarative = _usedDeclarative
            ? new DeclarativeModel<TState, TTrigger>(
                _contextSchemas,
                declaredInputs.ToDictionary(kv => kv.Key, kv => kv.Value.Schema),
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
        return declarative;
    }

    private List<ServerWrite> CollectServerWrites(
        Dictionary<TState, InvokeDefinition<TState>> invokes
    )
    {
        var serverWrites = _serverWrites
            .Concat(
                invokes.Values.SelectMany(invoke =>
                    Enum.GetValues<InvokeOutcomeKind>()
                        .SelectMany(kind =>
                            invoke
                                .EdgesFor(kind)
                                .Select(edge =>
                                    ServerWrite.Of(
                                        $"the outcome {invoke.TriggerName(kind)}",
                                        edge.Reduce,
                                        opaque: edge.Reduce is null
                                            && edge.CompiledReduce is not null
                                    )
                                )
                        )
                )
            )
            // The child's id, which the server writes when it starts the child.
            .Concat(
                invokes
                    .Values.Where(invoke => invoke.Spawn is not null)
                    .Select(invoke => new ServerWrite(
                        $"the start of {invoke.TrainType.Name} in {invoke.State}",
                        [invoke.Spawn.NotNull().ChildIdField]
                    ))
            )
            .ToList();
        return serverWrites;
    }

    private void RefuseOpenWritesAndServerOnlyRules(
        MachineRules rules,
        List<ServerWrite> serverWrites
    )
    {
        // Only a machine that declares rules is reached event by event, so only there is an open event a caller's.
        if (
            rules.Any
            && OpenWritesToServerFields(serverWrites).ToList() is { Count: > 0 } openWrites
        )
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(' ', openWrites.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
            );

        // A server-only trigger is never a client's, so it declares no client rule.
        foreach (var trigger in _serverOnly)
            if (rules.Events.TryGetValue(trigger.ToString(), out var clientRule))
                throw new InvalidOperationException(
                    $"The machine '{_id}' cannot be built: {trigger} is fired by the server only, and declares a "
                        + $"client rule ({clientRule})."
                );
    }

    private BuiltMachine<TState, TTrigger> BuildMachine(
        MachineDefinition<TState, TTrigger> definition,
        DeclarativeModel<TState, TTrigger>? declarative,
        Dictionary<TTrigger, (Type Type, ContextSchema Schema)> declaredInputs,
        MachineRules rules,
        List<ServerWrite> serverWrites
    )
    {
        return new BuiltMachine<TState, TTrigger>(definition, _committed, _effects, declarative)
        {
            SystemOwned = _systemOwned,
            ServerOnly = new HashSet<TTrigger>(_serverOnly),
            InvokedRunLimit = _invokedRunLimit,
            TriggerInputTypes = declaredInputs.ToDictionary(kv => kv.Key, kv => kv.Value.Type),
            ContextTypes = new Dictionary<TState, Type>(_contextTypes),
            Rules = rules,
            DelegateTriggers = _edgeInputs
                .Where(e => e.ReadsInputInCode)
                .Select(e => e.Trigger)
                .ToHashSet(),
            Start = _start,
            ServerWrites = serverWrites,
            BindsCustomHandlers = _customGuards.Count > 0 || _customReducers.Count > 0,
        };
    }

    // What one client event's edge writes to the context: every field its reduction writes, and those it copies from
    // the input. Either is null when the reducer is opaque (a delegate or a custom reducer), which may write any field
    // from the input.
    private sealed record EventWrite(
        TTrigger Trigger,
        CallerRule? Rule,
        IReadOnlyCollection<string>? Fields,
        IReadOnlyCollection<string>? FromInput,
        TState To
    );

    // The top-level fields a state's declared invariant requires to be absent: a reduction into that state that writes
    // one is refused by the invariant, so it cannot set what the server writes there.
    private HashSet<string> AbsentIn(TState state)
    {
        var absent = new HashSet<string>(StringComparer.Ordinal);
        if (_stateInvariants.TryGetValue(state, out var rules))
            foreach (var rule in rules)
                Collect(rule);
        return absent;

        void Collect(Rule rule)
        {
            switch (rule)
            {
                case Rule.Absent { Source: RuleSource.Context } field:
                    absent.Add(StartDeclaration.TopLevel(field.Field));
                    break;
                case Rule.All all:
                    foreach (var inner in all.Rules)
                        Collect(inner);
                    break;
            }
        }
    }

    // A field the server writes (what an event gated by a role or a policy writes, an effect's receipt, an invoked
    // train's outcome) is the server's to decide, as it is for the start: an event anyone may send (AnyAuthenticated,
    // Anonymous, or no rule) may not copy it from its input. One sentence per pair of an event and a server write.
    private IEnumerable<string> OpenWritesToServerFields(IReadOnlyList<ServerWrite> serverWrites)
    {
        foreach (var open in _eventWrites.Where(w => w.Rule is not { Gated: true }))
        {
            if (open.FromInput is { Count: 0 })
                continue;
            var absent = AbsentIn(open.To);
            foreach (
                var write in serverWrites
                    .Where(w => w.Fields is not { Count: 0 })
                    .DistinctBy(w => (w.Source, Fields: string.Join(',', w.Fields ?? [])))
            )
            {
                var overlap = (
                    open.FromInput is null || write.Fields is null
                        ? (open.FromInput ?? write.Fields ?? ["(any field)"]).ToList()
                        : open.FromInput.Intersect(write.Fields, StringComparer.Ordinal).ToList()
                )
                    .Where(f => !absent.Contains(f))
                    .ToList();
                if (overlap.Count == 0)
                    continue;
                var source = write.Source.TrimEnd(',');
                yield return $"the event {open.Trigger} ({open.Rule?.ToString() ?? "no rule"}) writes "
                    + string.Join(", ", overlap.Order(StringComparer.Ordinal).Select(f => $"'{f}'"))
                    + $" from its input, a field {source} writes. A caller {open.Trigger} admits would set what "
                    + $"only the server should. Leave the field out of {open.Trigger}'s reduction, or give "
                    + $"{open.Trigger} a rule that gates it.";
            }
        }
    }

    // One edge's rule as declared, before Build checks the event's edges agree.
    private sealed record EdgeRule(TState From, TTrigger Trigger, TState To, CallerRule? Rule);

    // Each event has one rule, whichever state it is sent from, so every edge of it declares the same one (or none
    // does). A gated event (one naming a role or a policy) may not enter the start state: anyone allowed to start an
    // instance can put one there, so the rule would gate nothing. A start state that invokes a train is the exception: entering
    // it runs the train, and the host compares the start's rule and every entering event's with the train's at
    // startup, which is the gate there.
    private MachineRules BuildRules()
    {
        var problems = new List<string>();
        var events = new Dictionary<string, CallerRule>(StringComparer.Ordinal);
        foreach (var group in _edgeRules.GroupBy(e => e.Trigger))
        {
            var edges = group.ToList();
            if (edges.TrueForAll(e => e.Rule is null))
                continue;
            var first = edges.First(e => e.Rule is not null).Rule.NotNull();
            if (edges.Exists(e => e.Rule is null || !e.Rule.SameAs(first)))
            {
                problems.Add(
                    $"the event {group.Key} declares different rules on its edges ("
                        + string.Join(
                            ", ",
                            edges.Select(e => $"from {e.From}: {e.Rule?.ToString() ?? "none"}")
                        )
                        + "). An event has one rule, whichever state it is sent from; declare the same one on "
                        + $"every edge of {group.Key}."
                );
                continue;
            }
            events[group.Key.ToString()] = first;
        }

        foreach (
            var edge in _edgeRules.Where(e =>
                e.Rule is { Gated: true }
                && EqualityComparer<TState>.Default.Equals(e.To, _initial.NotNull().Value)
                && !EqualityComparer<TState>.Default.Equals(e.From, e.To)
                && !_invokes.Exists(i => EqualityComparer<TState>.Default.Equals(i.State, e.To))
            )
        )
            problems.Add(
                $"the event {edge.Trigger} ({edge.Rule}) enters the start state {edge.To} from {edge.From}. Anyone "
                    + $"allowed to start an instance can put one in {edge.To}, so the rule would gate nothing. Send "
                    + "the event to another state, or drop its Requires and RequiresPolicy."
            );

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(' ', problems.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
            );

        return new MachineRules(
            events,
            _startRule?.Build(),
            _getRule?.Build(),
            _watchRule?.Build(),
            _discardRule?.Build()
        );
    }

    // One trigger's input as an edge declared it with WithInput, or nothing.
    private sealed record EdgeInput(
        TTrigger Trigger,
        TState From,
        Type? Type,
        ContextSchema? Schema,
        bool ReadsInputInCode,
        bool RunsEffect
    );

    // The input record each trigger declares. One trigger has one input schema, which both engines validate every
    // edge's input against, so every edge of a trigger declares the same record, or none does.
    private Dictionary<TTrigger, (Type Type, ContextSchema Schema)> DeclaredInputs()
    {
        var declared = new Dictionary<TTrigger, (Type Type, ContextSchema Schema)>();
        foreach (var group in _edgeInputs.GroupBy(e => e.Trigger))
        {
            var types = group.Select(e => e.Type).Distinct().ToList();
            if (types.Count > 1)
                throw new InvalidOperationException(
                    $"The machine '{_id}' declares the trigger {group.Key} with different inputs on its edges ("
                        + string.Join(
                            ", ",
                            group.Select(e =>
                                $"from {e.From}: {(e.Type is null ? "no WithInput" : e.Type.Name)}"
                            )
                        )
                        + $"). One trigger has one input schema, which both engines validate its input against. "
                        + $"Declare the same WithInput<T>() on every edge of {group.Key}, or on none."
                );
            if (group.First() is { Type: { } type, Schema: { } schema })
                declared[group.Key] = (type, schema);
        }
        return declared;
    }

    // What the engine checks each trigger's input against. A declared record is checked as strictly as its CLR
    // type. On a declarative machine, a trigger that declares none takes no input, except an effect-bound one,
    // which takes the receipt the effect runner hands it. A trigger whose guard or reducer is a delegate reads
    // the input in code nothing can describe, so it is left to that code; so is every trigger of a machine built
    // from delegates alone.
    private Dictionary<TTrigger, ContextSchema> InputSchemas(
        IReadOnlyDictionary<TTrigger, (Type Type, ContextSchema Schema)> declared
    )
    {
        var schemas = new Dictionary<TTrigger, ContextSchema>();
        foreach (var group in _edgeInputs.GroupBy(e => e.Trigger))
        {
            if (declared.TryGetValue(group.Key, out var input))
                schemas[group.Key] = input.Schema;
            else if (!_usedDeclarative || group.Any(e => e.ReadsInputInCode))
                continue;
            else if (group.Any(e => e.RunsEffect))
                schemas[group.Key] = ReceiptInput;
            else
                schemas[group.Key] = ContextSchema.Empty;
        }
        return schemas;
    }

    /// <summary>
    /// The input an effect-bound trigger that declares no record takes: the receipt the effect runner hands it,
    /// or nothing. The TypeScript twin derives the same schema from an edge's <c>effect</c> in the IR.
    /// </summary>
    internal static readonly ContextSchema ReceiptInput = new([
        new FieldSchema("receipt", JsonFieldType.String, Nullable: true, []),
    ]);

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

    // The server supplies an effect-bound trigger's input and refuses an advance of it, and the twin and the generated
    // client treat the trigger as effect-bound wherever it is sent. A plain edge on the same trigger would take an
    // input the client cannot send and a prediction the twin does not make, so the trigger is sent on the effect's
    // edge only.
    private void RefuseOtherEdgesOnEffectTriggers()
    {
        foreach (var effect in _effects)
        foreach (var t in _transitions)
        {
            if (!EqualityComparer<TTrigger>.Default.Equals(t.Trigger, effect.Trigger))
                continue;
            if (
                EqualityComparer<TState>.Default.Equals(t.From, effect.From)
                && EqualityComparer<TState>.Default.Equals(t.To, effect.To)
            )
                continue;
            throw new InvalidOperationException(
                $"The machine '{_id}' sends {t.Trigger} on {t.From} -> {t.To}, but {t.Trigger} runs the effect bound "
                    + $"with RunsOnce on {effect.From} -> {effect.To}. The trigger an effect is bound to is sent on "
                    + "that edge only; give the other edge a trigger of its own."
            );
        }
    }

    // An effect is pending on its instance's row while it runs, and no other write changes the row: an invoked train's
    // outcome could then not be applied to its invoking state, and the effect's own write would have to queue or cancel
    // a run. So an effect neither runs from nor lands in a state that invokes a train.
    private void RefuseEffectsOnInvokingStates()
    {
        foreach (var effect in _effects)
        foreach (var invoke in _invokes)
        {
            if (
                !EqualityComparer<TState>.Default.Equals(invoke.State, effect.From)
                && !EqualityComparer<TState>.Default.Equals(invoke.State, effect.To)
            )
                continue;
            throw new InvalidOperationException(
                $"The machine '{_id}' binds an effect with RunsOnce on {effect.From} -> {effect.Trigger} -> "
                    + $"{effect.To}, and {invoke.State} invokes {invoke.TrainType.Name}. An effect neither runs from "
                    + "nor lands in a state that invokes a train; put a plain state between them."
            );
        }
    }

    // Every invoking state says where each outcome goes, and invokes one train. A missing OnCancelled would leave
    // a cancelled run with no declared edge, and an operator's cancel is not "the failure state" once a machine
    // has several; a second Invokes on one state would give one entry two runs and one token.
    private Dictionary<TState, InvokeDefinition<TState>> BuildInvokes()
    {
        var problems = new List<string>();
        AddSharedStateProblems(problems);
        foreach (var draft in _invokes)
            AddDraftProblems(problems, draft);

        // The success outcome's input is the train's output, validated against its type's schema like a trigger's
        // input, so the output type needs one.
        var outputSchemas = OutputSchemas(problems);

        foreach (
            var state in _diffOutcomeSamples.Keys.Where(s =>
                _invokes.TrueForAll(i => !i.State.Equals(s))
            )
        )
            problems.Add(
                $"the differential declares an outcome sample for {state}, which invokes no train. Declare "
                    + "OutcomeSample only for a state with Invokes."
            );

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(
                        ' ',
                        problems
                            .Distinct(StringComparer.Ordinal)
                            .Select(p => char.ToUpperInvariant(p[0]) + p[1..])
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
            {
                OutputSchema = outputSchemas[d],
                Spawn = d.Spawn,
            }
        );
    }

    // A state invokes, or spawns, one thing at most.
    private void AddSharedStateProblems(List<string> problems)
    {
        foreach (var group in _invokes.GroupBy(i => i.State))
        {
            var drafts = group.ToList();
            if (drafts.Count > 1)
                problems.Add(
                    drafts.TrueForAll(d => d.Spawn is null)
                        ? $"the state {group.Key} invokes {drafts.Count} trains "
                            + $"({string.Join(", ", drafts.Select(d => d.TrainType.Name))}), but a state invokes at "
                            + "most one. Give each train a state of its own."
                        : $"the state {group.Key} invokes or spawns {drafts.Count} things "
                            + $"({string.Join(", ", drafts.Select(d => d.TrainType.Name))}), but a state invokes "
                            + "or spawns at most one. Give each its own state."
                );
        }
    }

    private void AddDraftProblems(List<string> problems, InvokeDraft draft)
    {
        var state = draft.State;
        var train = draft.TrainType.Name;
        if (draft.Spawn is { } spawn)
            problems.AddRange(SpawnProblems(state, spawn));
        else if (!draft.TrainType.IsInterface)
            problems.Add(
                $"the state {state} invokes {train}, which is a class. Name the train by its interface "
                    + "(Invokes<IMyTrain, TInput, TOutput>), which is the train's canonical name."
            );
        // A value tuple's elements are fields, which neither the output's stored JSON nor its exported schema
        // carries: every guard and reduction would read nothing, and the run would take whichever OnDone edge
        // has no guard, its reductions writing null.
        if (IsValueTuple(draft.OutputType))
            problems.Add(
                $"the state {state} invokes {train}, whose output {draft.OutputType.Name} is a tuple. A tuple's "
                    + "elements are fields, which the output's JSON does not carry, so no OnDone guard or "
                    + "reduction could read them. Return a record with properties instead."
            );
        if (draft.Done.Count == 0)
            problems.Add(
                $"the state {state} {draft.Doing} but declares no OnDone. Add `.OnDone({typeof(TState).Name}.X)` "
                    + "for where a successful run goes."
            );
        problems.AddRange(
            ExactlyOnce(draft.Failed.Count, state, draft.Doing, "OnFailed", "a failed")
        );
        problems.AddRange(
            ExactlyOnce(draft.Cancelled.Count, state, draft.Doing, "OnCancelled", "a cancelled")
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

    private Dictionary<InvokeDraft, ContextSchema> OutputSchemas(List<string> problems)
    {
        var outputSchemas = new Dictionary<InvokeDraft, ContextSchema>(
            ReferenceEqualityComparer.Instance
        );
        foreach (var draft in _invokes.Where(d => !IsValueTuple(d.OutputType)))
            try
            {
                outputSchemas[draft] = SchemaReflection.ForInput(draft.OutputType);
            }
            catch (InvalidOperationException ex)
            {
                problems.Add(
                    $"the state {draft.State} invokes {draft.TrainType.Name}, whose output "
                        + $"{draft.OutputType.Name} has no schema to check it against: {ex.Message}"
                );
            }
        return outputSchemas;
    }

    // The child's id goes into a field the state's declared context holds, so the snapshot a client reads names it. A
    // state that declares no context leaves the record the selector names as the only description.
    private IEnumerable<string> SpawnProblems(TState state, SpawnDefinition spawn)
    {
        if (!_contextSchemas.ContainsKey(state))
            yield break;
        var declared = _contextTypes.GetValueOrDefault(state);
        var field = declared
            ?.GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            )
            .FirstOrDefault(p =>
                string.Equals(
                    MemberPath.JsonNameOf(p),
                    spawn.ChildIdField,
                    StringComparison.Ordinal
                )
            );
        if (field is null)
            yield return $"the state {state} spawns {spawn.ChildMachine.Name} and writes its id into "
                + $"'{spawn.ChildIdField}', which is not a field of its context "
                + $"({declared?.Name ?? "an empty context"}). Declare it on the state's context as a Guid?.";
        else if (field.PropertyType != typeof(Guid?) && field.PropertyType != typeof(Guid))
            yield return $"the state {state} spawns {spawn.ChildMachine.Name} and writes its id into "
                + $"'{spawn.ChildIdField}', which {declared.NotNull().Name} declares as a {field.PropertyType.Name}. "
                + "Declare it as a Guid?.";
    }

    // A state a child ends in ends it one way, and the child does not wait on anything there: an end that also
    // queued a run or started a child would hand its parent an outcome while work it started is still going.
    private void RefuseConflictingEnds(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes
    )
    {
        var problems = _endConflicts
            .Select(state =>
                $"the state {state} is declared with both Ends() and Fails(). A child ends one way in a state; "
                + "give each outcome a state of its own."
            )
            .Concat(
                _ends
                    .Keys.Where(invokes.ContainsKey)
                    .Select(state =>
                        $"the state {state} is an end ({_ends[state]}) and {(invokes[state].Spawn is null ? "invokes" : "spawns")} "
                        + $"{invokes[state].TrainType.Name}. A state a child ends in waits on nothing."
                    )
            )
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(' ', problems.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
            );
    }

    private static bool IsValueTuple(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsValueType
            && typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(underlying);
    }

    private static IEnumerable<string> ExactlyOnce(
        int count,
        TState state,
        string doing,
        string method,
        string run
    )
    {
        if (count == 0)
            yield return $"the state {state} {doing} but declares no {method}. Every invoking state says "
                + $"where {run} run goes; add `.{method}({typeof(TState).Name}.X)`.";
        else if (count > 1)
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"the state {state} declares {method} {count} times. Declare it exactly once."
            );
    }

    // An outcome target means "the train produced this": the draft reaches it with the context its outcome
    // reduced. An ordinary transition into it would let a client put a draft there with no run, carrying whatever
    // its own input reduced, so the machine is refused, as it is for an effect's target. A self-loop on the
    // target does not enter it and is allowed. So is an edge into a target that itself invokes a train: entering
    // it only queues a new run and records no result, and it is how a chained stage is retried.
    private void RefuseOtherEdgesIntoOutcomeTargets(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes
    )
    {
        // Checked first, because its cause is not the edge: an outcome target joins the reserved states, which no
        // save may create a draft in, so a machine that starts at one could never have a draft at all.
        foreach (var invoke in invokes.Values)
        foreach (var target in invoke.Targets)
            if (
                EqualityComparer<TState>.Default.Equals(target, _initial.NotNull().Value)
                && !invokes.ContainsKey(target)
            )
                throw new InvalidOperationException(
                    $"The machine '{_id}' starts at {target}, but {target} is also where the outcome of "
                        + $"{invoke.TrainType.Name}, invoked in {invoke.State}, goes. An outcome target is reserved: "
                        + "no save may create a draft in it, so no draft of this machine could ever be created. Send "
                        + "the outcome to a state of its own, with an edge from there back to the start if the "
                        + "flow returns to it."
                );

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

    private sealed record TimerField(string Name);

    // Every timer names where it goes, waits a positive, bounded time, and reads its duration from a field the state's
    // context really has, typed as a duration and not sensitive (a deadline is exported and shown). A state has one
    // timer; a timer back into its own state would fire forever, and so would timers that only lead to each other,
    // so one event at least must break every loop. A timer into an effect's target or an outcome's target would put
    // the instance where only the effect or the run may: the same edges are refused for an ordinary transition.
    private Dictionary<TState, TimerDefinition<TState>> BuildTimers(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes
    )
    {
        var problems = new List<string>();
        foreach (var group in _timers.GroupBy(t => t.State))
            if (group.Skip(1).Any())
                problems.Add(
                    $"the state {group.Key} declares {(group.Take(3).Count() == 2 ? "two" : group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                        + "timers, but a state has at most one. Keep one After(...) on it."
                );

        var built = new Dictionary<TState, TimerDefinition<TState>>();
        foreach (var draft in _timers)
            BuildTimer(draft, invokes, problems, built);

        problems.AddRange(TimerCycles(built));

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(
                        ' ',
                        problems
                            .Distinct(StringComparer.Ordinal)
                            .Select(p => char.ToUpperInvariant(p[0]) + p[1..])
                    )
            );
        return built;
    }

    // Checks one timer and, when it has a target that is not its own state, adds it to `built`.
    private void BuildTimer(
        TimerDraft draft,
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes,
        List<string> problems,
        Dictionary<TState, TimerDefinition<TState>> built
    )
    {
        var state = draft.State;
        if (draft.Target is not { } target)
        {
            problems.Add(
                $"the timer of {state} has no target. Finish After(...) with `.GoTo({typeof(TState).Name}.X)`."
            );
            return;
        }
        if (EqualityComparer<TState>.Default.Equals(target, state))
        {
            problems.Add(
                $"the timer of {state} goes back to {state}, so it would fire again every time it fired. Send it "
                    + "to another state, or restart the wait with an event that enters the state."
            );
            return;
        }
        AddTimerTargetProblems(state, target, invokes, problems);

        var (field, computed) = TimerDurationOf(draft, problems);

        built[state] = new TimerDefinition<TState>(
            state,
            target,
            draft.Fixed,
            field?.Name,
            draft.Fixed ?? draft.Min,
            draft.Fixed ?? draft.Max,
            computed
        );
    }

    private void AddTimerTargetProblems(
        TState state,
        TState target,
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes,
        List<string> problems
    )
    {
        if (
            _effects.FirstOrDefault(e => EqualityComparer<TState>.Default.Equals(e.To, target)) is
            { } effect
        )
            problems.Add(
                $"the timer of {state} goes to {target}, which is the target of the effect bound with RunsOnce on "
                    + $"{effect.From} -> {effect.Trigger}. Only the effect may enter the state that records its "
                    + "receipt; send the timer to another state."
            );
        if (
            !invokes.ContainsKey(target)
            && invokes.Values.FirstOrDefault(i => i.Targets.Contains(target)) is { } invoke
        )
            problems.Add(
                $"the timer of {state} goes to {target}, which is where the outcome of {invoke.TrainType.Name}, "
                    + $"invoked in {invoke.State}, goes. Only the outcome may enter it; send the timer to another "
                    + "state."
            );
    }

    // A fixed wait, or a bounded one read from the context.
    private (TimerField? Field, Func<JsonObject, TimeSpan?>? Computed) TimerDurationOf(
        TimerDraft draft,
        List<string> problems
    )
    {
        var state = draft.State;
        TimerField? field = null;
        Func<JsonObject, TimeSpan?>? computed = null;
        if (draft.Fixed is { } fixedDuration)
        {
            if (fixedDuration <= TimeSpan.Zero)
                problems.Add(
                    $"the timer of {state} waits {fixedDuration}, but a timer waits a positive time."
                );
            else if (fixedDuration > MachineTimers.MaxDuration)
                problems.Add(MachineTimers.TooLong(state.ToString(), fixedDuration));
        }
        else
        {
            if (draft.Min <= TimeSpan.Zero || draft.Min > draft.Max)
                problems.Add(
                    $"the timer of {state} is bounded by {draft.Min} to {draft.Max}, but its minimum must be "
                        + "positive and no more than its maximum."
                );
            else if (draft.Max > MachineTimers.MaxDuration)
                problems.Add(
                    $"the timer of {state} may wait up to {draft.Max}, longer than the "
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $"{MachineTimers.MaxDuration.TotalDays} days a timer may wait (MachineTimers.MaxDuration)."
                        )
                );
            (field, computed) = TimerDuration(draft, problems);
        }
        return (field, computed);
    }

    // The field a context duration reads, or the delegate it compiles to when its selector is not a single field. The
    // field is checked against the state's declared context: it must be one of its fields, typed as a duration, and
    // not sensitive. A state that declares no context leaves the record the selector names as the only description.
    private (TimerField? Field, Func<JsonObject, TimeSpan?>? Computed) TimerDuration(
        TimerDraft draft,
        List<string> problems
    )
    {
        var state = draft.State;
        var selector = draft.Duration.NotNull();
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } u
            ? u.Operand
            : selector.Body;
        if (
            body is not MemberExpression { Member: System.Reflection.PropertyInfo property } member
            || member.Expression is not ParameterExpression
        )
            return (null, ComputedDuration(selector));

        var name = MemberPath.JsonNameOf(property);
        if (Sensitive(property))
        {
            problems.Add(
                $"the timer of {state} reads its duration from '{name}', which is marked [TraxSensitive]. A deadline "
                    + "is exported and shown to the client, so it cannot come from a sensitive field."
            );
            return (null, null);
        }

        if (_contextSchemas.ContainsKey(state))
        {
            var declared = _contextTypes.GetValueOrDefault(state);
            var field = declared
                ?.GetProperties(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                )
                .FirstOrDefault(p =>
                    string.Equals(MemberPath.JsonNameOf(p), name, StringComparison.Ordinal)
                );
            if (field is null)
            {
                problems.Add(
                    $"the timer of {state} reads its duration from '{name}', which is not a field of {state}'s "
                        + $"context ({declared?.Name ?? "an empty context"}). Read a field the state's context declares."
                );
                return (null, null);
            }
            if (field.PropertyType != typeof(TimeSpan) && field.PropertyType != typeof(TimeSpan?))
            {
                problems.Add(
                    $"the timer of {state} reads its duration from '{name}', which {declared.NotNull().Name} declares as a "
                        + $"{field.PropertyType.Name}. A duration field is a TimeSpan? (an ISO-8601 duration on the wire)."
                );
                return (null, null);
            }
            if (Sensitive(field))
            {
                problems.Add(
                    $"the timer of {state} reads its duration from '{name}', which is marked [TraxSensitive]. A "
                        + "deadline is exported and shown to the client, so it cannot come from a sensitive field."
                );
                return (null, null);
            }
        }

        return (new TimerField(name), null);
    }

    private static bool Sensitive(System.Reflection.PropertyInfo property) =>
        property.IsDefined(typeof(Trax.Effect.Attributes.TraxSensitiveAttribute), inherit: true);

    // A selector that is not a single field runs over the context read as its record. The IR cannot carry it, so the
    // export refuses the machine; the server still bounds what it returns.
    private static Func<JsonObject, TimeSpan?> ComputedDuration(LambdaExpression selector)
    {
        var compiled = selector.Compile();
        var record = selector.Parameters[0].Type;
        return context =>
            compiled.DynamicInvoke(JsonSerializer.Deserialize(context, record, TimerContextJson))
            as TimeSpan?;
    }

    // How a computed duration reads the context: the camelCase names the schema reflects, an enum by its C# member
    // name (the spelling the API stores; the read is case-insensitive, so a context that carries the older camelCase
    // spelling still reads), and a TimeSpan written as an ISO-8601 duration, as a context carries it.
    private static readonly JsonSerializerOptions TimerContextJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new IsoDurationJsonConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    // A loop made only of timers, which the instance would go round forever: TimerLoops, which derivation reads too.
    private static IEnumerable<string> TimerCycles(
        IReadOnlyDictionary<TState, TimerDefinition<TState>> timers
    ) =>
        TimerLoops
            .Find(timers.ToDictionary(t => t.Key, t => t.Value.Target), Enum.GetValues<TState>())
            .Select(TimerLoops.Describe);

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

    // A comparison reads a number, so on a field whose schema holds something else (an enum, a string, a DateTime,
    // which are strings on the wire) it is false for every value: a guard that never holds, built without a word.
    // Refuse it, naming the field and its type. A field the schema does not describe (an untyped context, a field it
    // does not list) is left alone. A bound that is not finite is refused too, for a Rule.Compare written by hand.
    private void RefuseUnreadableComparisons(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes,
        IReadOnlyDictionary<TTrigger, ContextSchema> inputSchemas
    )
    {
        var problems = new List<string>();
        foreach (var t in _declarativeTransitions)
            if (t.Guard is { } guard)
                CheckComparisons(
                    problems,
                    guard,
                    $"the guard of {t.From} -> {t.Trigger}",
                    t.From,
                    inputSchemas.GetValueOrDefault(t.Trigger),
                    $"the input of {t.Trigger}"
                );
        foreach (var (state, invariants) in _stateInvariants)
        foreach (var rule in invariants)
            CheckComparisons(
                problems,
                rule,
                $"the Requires of {state}",
                state,
                input: null,
                inputName: null
            );
        foreach (var invoke in invokes.Values)
        foreach (var edge in invoke.Done)
            if (edge.Guard is { } guard)
                CheckComparisons(
                    problems,
                    guard,
                    $"the OnDone guard of {invoke.State} -> {edge.To}",
                    invoke.State,
                    invoke.OutputSchema,
                    $"the output of {invoke.TrainType.Name}"
                );

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(
                        ' ',
                        problems
                            .Distinct(StringComparer.Ordinal)
                            .Select(p => char.ToUpperInvariant(p[0]) + p[1..])
                    )
            );
    }

    private void CheckComparisons(
        List<string> problems,
        Rule rule,
        string site,
        TState state,
        ContextSchema? input,
        string? inputName
    )
    {
        switch (rule)
        {
            case Rule.All all:
                foreach (var inner in all.Rules)
                    CheckComparisons(problems, inner, site, state, input, inputName);
                break;
            case Rule.Any any:
                foreach (var inner in any.Rules)
                    CheckComparisons(problems, inner, site, state, input, inputName);
                break;
            case Rule.Compare compare:
                var method = CompareMethod(compare.Op);
                if (!double.IsFinite(compare.Value))
                {
                    problems.Add(
                        $"{site} compares '{compare.Field}' with {method}"
                            + $"({compare.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}), "
                            + "but a comparison needs a finite bound. NaN makes every comparison false, and "
                            + "the IR has no number for an infinity."
                    );
                    break;
                }
                var (schema, owner) =
                    compare.Source == RuleSource.Input
                        ? (input, inputName)
                        : (_contextSchemas.GetValueOrDefault(state), $"the context of {state}");
                var described = schema?.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, compare.Field, StringComparison.Ordinal)
                );
                if (
                    described is not null
                    && described.Type is not (JsonFieldType.Number or JsonFieldType.Integer)
                )
                    problems.Add(
                        $"{site} compares the field '{compare.Field}' of {owner} with {method}, but its "
                            + $"schema type is {described.Type.ToString().ToLowerInvariant()}, not a number, "
                            + "so the comparison would be false for every value. Compare a number field, or "
                            + "use a rule for its type (IsOneOf for an enum or a string)."
                    );
                break;
        }
    }

    private static string CompareMethod(CompareOp op) =>
        op switch
        {
            CompareOp.GreaterThan => "GreaterThan",
            CompareOp.GreaterOrEqual => "AtLeast",
            CompareOp.LessThan => "LessThan",
            CompareOp.LessOrEqual => "AtMost",
            _ => "EqualTo",
        };

    private static IEnumerable<string> CustomNames(Rule rule) =>
        rule switch
        {
            Rule.Custom c => [c.Name],
            Rule.All all => all.Rules.SelectMany(CustomNames),
            Rule.Any any => any.Rules.SelectMany(CustomNames),
            _ => [],
        };

    private sealed class StateBuilder(MachineBuilder<TState, TTrigger> owner, TState state)
        : IStateBuilder<TState, TTrigger>,
            IIrStateBuilder<TState, TTrigger>
    {
        public IStateBuilder<TState, TTrigger> Holds(Func<JsonObject, string?> validator)
        {
            owner._validators[state] = validator;
            owner._delegateValidators.Add(state);
            return this;
        }

        public IStateBuilder<TState, TTrigger> Context<TContext>()
        {
            owner._contextTypes[state] = typeof(TContext);
            return SetSchema(SchemaReflection.For<TContext>());
        }

        public IStateBuilder<TState, TTrigger> Context()
        {
            owner._contextTypes.Remove(state);
            return SetSchema(ContextSchema.Empty);
        }

        public IStateBuilder<TState, TTrigger> Context(Type record, ContextSchema schema)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(schema);
            owner._contextTypes[state] = record;
            return SetSchema(schema);
        }

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
                (context, _) => input(context)
            );
            owner._invokes.Add(draft);
            return new InvokeBuilder(owner, this, draft);
        }

        public IInvokeBuilder<TState, TTrigger> InvokesAsOwner<TTrain, TInput, TOutput>(
            Func<JsonObject, string?, TInput> input
        )
            where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput>
        {
            ArgumentNullException.ThrowIfNull(input);
            var draft = new InvokeDraft(
                state,
                typeof(TTrain),
                typeof(TInput),
                typeof(TOutput),
                (context, ownerKey) => input(context, ownerKey)
            );
            owner._invokes.Add(draft);
            return new InvokeBuilder(owner, this, draft);
        }

        public IInvokeBuilder<TState, TTrigger> Spawns<TChild, TContext, TOutput>(
            Expression<Func<TContext, Guid?>> childId,
            Func<JsonObject, JsonObject>? context = null
        )
            where TChild : class
        {
            ArgumentNullException.ThrowIfNull(childId);
            var draft = new InvokeDraft(
                state,
                typeof(TChild),
                typeof(JsonObject),
                typeof(TOutput),
                (_, _) => null
            )
            {
                Spawn = new SpawnDefinition(typeof(TChild), MemberPath.Of(childId), context),
            };
            owner._invokes.Add(draft);
            return new InvokeBuilder(owner, this, draft);
        }

        public IStateBuilder<TState, TTrigger> Ends() => End(InvokeOutcomeKind.Done);

        public IStateBuilder<TState, TTrigger> Fails() => End(InvokeOutcomeKind.Failed);

        private IStateBuilder<TState, TTrigger> End(InvokeOutcomeKind kind)
        {
            if (owner._ends.TryGetValue(state, out var declared) && declared != kind)
                owner._endConflicts.Add(state);
            owner._ends[state] = kind;
            return this;
        }

        public ITimerBuilder<TState, TTrigger> After(TimeSpan delay)
        {
            var draft = new TimerDraft(state, delay, duration: null, delay, delay);
            owner._timers.Add(draft);
            return new TimerBuilder(this, draft);
        }

        public ITimerBuilder<TState, TTrigger> After<TContext>(
            Expression<Func<TContext, TimeSpan?>> duration,
            TimeSpan min,
            TimeSpan max
        )
        {
            ArgumentNullException.ThrowIfNull(duration);
            var draft = new TimerDraft(state, fixedDuration: null, duration, min, max);
            owner._timers.Add(draft);
            return new TimerBuilder(this, draft);
        }
    }

    // One Invokes as declared, before Build checks it.
    private sealed record InvokeDraft(
        TState State,
        Type TrainType,
        Type InputType,
        Type OutputType,
        Func<JsonObject, string?, object?> CreateInput
    )
    {
        /// <summary>The child machine a spawning state starts; null for a state that invokes a train.</summary>
        public SpawnDefinition? Spawn { get; init; }

        /// <summary>What the state does, as a refusal names it: "invokes IMyTrain" or "spawns MyMachine".</summary>
        public string Doing => $"{(Spawn is null ? "invokes" : "spawns")} {TrainType.Name}";

        public List<OutcomeEdge<TState>> Done { get; } = [];
        public List<OutcomeEdge<TState>> Failed { get; } = [];
        public List<OutcomeEdge<TState>> Cancelled { get; } = [];
    }

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
            draft.Failed.Add(Edge(target, when: null, reduce));
            return this;
        }

        public IInvokeBuilder<TState, TTrigger> OnCancelled(TState target, Reduction? reduce = null)
        {
            draft.Cancelled.Add(Edge(target, when: null, reduce));
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

        public IInvokeBuilder<TState, TTrigger> Spawns<TChild, TContext, TOutput>(
            Expression<Func<TContext, Guid?>> childId,
            Func<JsonObject, JsonObject>? context = null
        )
            where TChild : class => state.Spawns<TChild, TContext, TOutput>(childId, context);

        public IStateBuilder<TState, TTrigger> Ends() => state.Ends();

        public IStateBuilder<TState, TTrigger> Fails() => state.Fails();

        public ITimerBuilder<TState, TTrigger> After(TimeSpan delay) => state.After(delay);

        public ITimerBuilder<TState, TTrigger> After<TContext>(
            Expression<Func<TContext, TimeSpan?>> duration,
            TimeSpan min,
            TimeSpan max
        ) => state.After(duration, min, max);
    }

    private sealed class TransitionBuilder(
        MachineBuilder<TState, TTrigger> owner,
        IStateBuilder<TState, TTrigger> state,
        TState from,
        TTrigger trigger
    ) : ITransitionBuilder<TState, TTrigger>, IIrTransitionBuilder<TState, TTrigger>
    {
        private Func<JsonObject, JsonNode?, bool>? _guard;
        private string? _guardMessage;
        private Func<JsonObject, JsonNode?, JsonObject>? _reduce;
        private Type? _effectType;
        private string? _effectKeyPrefix;
        private Rule? _guardRule;
        private Reduction? _reduction;
        private Type? _inputType;
        private ContextSchema? _inputSchema;
        private readonly CallerRuleDraft _rule = new(
            $"The machine '{owner._id}' event {trigger} from {from}",
            read: false
        );

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
            // Reflected here, so a record with no finite schema (one that contains itself) refuses at once,
            // naming the record.
            _inputSchema = SchemaReflection.ForInput(typeof(TInput));
            _inputType = typeof(TInput);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> WithInput(Type record, ContextSchema schema)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(schema);
            owner._usedDeclarative = true;
            _inputSchema = schema;
            _inputType = record;
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
            RecordRuleAndWrites(target);
            return state;
        }

        // The edge's client rule, what the server alone writes on it, and what it reads as input.
        private void RecordRuleAndWrites(TState target)
        {
            var rule = _rule.Build();
            owner._edgeRules.Add(new EdgeRule(from, trigger, target, rule));
            // What only the server writes: the effect's receipt, and what a gated event (a role or a policy) writes.
            if (_effectType is not null || rule is { Gated: true })
                owner._serverWrites.Add(
                    ServerWrite.Of(
                        _effectType is not null
                            ? $"the effect-bound event {trigger}"
                            : $"the event {trigger}, gated by {rule},",
                        _reduction,
                        opaque: _reduce is not null && _reduction is null
                    )
                );
            if (_effectType is null)
                owner._eventWrites.Add(
                    new EventWrite(
                        trigger,
                        rule,
                        _reduce is not null && _reduction is null
                            ? null
                            : ServerWrite.Of(string.Empty, _reduction, opaque: false).Fields,
                        _reduce is not null && _reduction is null
                            ? null
                            : StartDeclaration.FieldsFromInput(_reduction),
                        target
                    )
                );
            owner._edgeInputs.Add(
                new EdgeInput(
                    trigger,
                    from,
                    _inputType,
                    _inputSchema,
                    ReadsInputInCode: (_guard is not null && _guardRule is null)
                        || (_reduce is not null && _reduction is null),
                    RunsEffect: _effectType is not null
                )
            );
        }

        public ITransitionBuilder<TState, TTrigger> Requires(params string[] roles)
        {
            _rule.Requires(roles);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> RequiresPolicy(string policy)
        {
            _rule.RequiresPolicy(policy);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> AnyAuthenticated()
        {
            _rule.Kind(CallerRuleKind.AnyAuthenticated);
            return this;
        }

        public ITransitionBuilder<TState, TTrigger> Anonymous()
        {
            _rule.Kind(CallerRuleKind.Anonymous);
            return this;
        }
    }

    private sealed class DifferentialBuilder(MachineBuilder<TState, TTrigger> owner)
        : IDifferentialBuilder<TState, TTrigger>
    {
        // camelCase field names to match the names the guards/reducers read (SchemaReflection uses the same policy),
        // but enums by their C# member name, the one spelling the API stores (MachineSurface.Json); nulls kept so a
        // nullable field's null appears in a probe context exactly as the wire carries it. An empty input uses
        // EmptySample, so all-null typed inputs are never how {} is authored.
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
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
            if (!owner._diffSeeds.TryGetValue(state, out var list))
                owner._diffSeeds[state] = list = [];
            if (list.Exists(seed => JsonNode.DeepEquals(seed, context)))
                owner._diffSeedDuplicates.Add(state);
            else
                list.Add(context);
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
