using System.Linq.Expressions;
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

    /// <summary>The states with a timer, keyed by state. Empty for a machine with none.</summary>
    internal IReadOnlyDictionary<TState, TimerDefinition<TState>> Timers => Definition.Timers;

    /// <summary>
    /// The rules a timer acts under, by its trigger: the union of the rules of everything that enters the timed state,
    /// as each is written for the census (<c>Requires(Editor)</c>), sorted. An event entering it contributes its rule
    /// (<c>undeclared</c> when it declares none), the start its rule when the state is the initial one, and a timer
    /// or an invoked train's outcome entering it the rules its own source state was entered under. A timer carries
    /// the context unchanged, but it acts for whoever could have put the instance in its state.
    /// </summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> TimerRules()
    {
        var entered = new Dictionary<TState, SortedSet<string>>();
        SortedSet<string> Of(TState state) =>
            entered.TryGetValue(state, out var set)
                ? set
                : entered[state] = new SortedSet<string>(StringComparer.Ordinal);
        string Text(CallerRule? rule) => rule?.ToString() ?? "undeclared";

        foreach (var t in Definition.Transitions)
            Of(t.To).Add(Text(Rules.Events.GetValueOrDefault(t.Trigger.ToString()!)));
        if (Rules.Start is not null || Start is not null)
            Of(Definition.InitialState).Add(Text(Rules.Start));

        // What a timer or an outcome carries into its target is whatever its source was entered under, so the sets
        // are widened until nothing changes; each pass only adds, and the sets are finite.
        var server = Timers
            .Values.Select(t => (From: t.State, t.Target))
            .Concat(
                Invokes.Values.SelectMany(i => i.Targets.Select(to => (From: i.State, Target: to)))
            )
            .ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var (from, to) in server)
                if (entered.TryGetValue(from, out var source))
                    foreach (var rule in source.ToList())
                        changed |= Of(to).Add(rule);
        } while (changed);

        return Timers.Values.ToDictionary(
            t => t.TriggerName,
            t =>
                (IReadOnlyList<string>)(
                    entered.TryGetValue(t.State, out var rules) ? rules.ToList() : []
                ),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// The states only the server may put a draft in or take it out of, so a soft autosave may not, and its owner may
    /// not discard an instance in one: committed states, effect targets, states that invoke a train, every target an
    /// invoked train's outcome reaches, and every target of an event gated by a role or a policy (<c>Requires</c> or
    /// <c>RequiresPolicy</c>), which records a decision only the server's gate let through. A state any of these
    /// reaches is reserved even when an ungated event reaches it too. A timer's target is not one: a timer carries
    /// the context unchanged. The timed state itself is locked instead (see the draft service), so autosave cannot
    /// leave or rewrite it.
    /// </summary>
    internal IReadOnlySet<TState> ReservedStates =>
        CommittedStates
            .Concat(Effects.Select(e => e.To))
            .Concat(Invokes.Keys)
            .Concat(Invokes.Values.SelectMany(i => i.Targets))
            .Concat(
                Definition
                    .Transitions.Where(t =>
                        Rules.Events.GetValueOrDefault(t.Trigger.ToString()!) is { Gated: true }
                    )
                    .Select(t => t.To)
            )
            .ToHashSet();

    /// <summary>
    /// Whether the machine's instances belong to the system: <c>IMachineInstances.Start</c> creates them, and no
    /// user's draft operation reaches the machine. Declared with <see cref="IMachineBuilder{TState,TTrigger}.SystemOwned"/>.
    /// </summary>
    internal bool SystemOwned { get; init; }

    /// <summary>
    /// The most live invoked runs one user may hold, counted across every machine, for entering one of this
    /// machine's invoking states to be allowed. Declared with <see cref="IMachineBuilder{TState,TTrigger}.InvokedRunLimit"/>; system owners are not
    /// capped.
    /// </summary>
    internal int InvokedRunLimit { get; init; } = DefaultInvokedRunLimit;

    /// <summary>The per-user live invoked run limit a machine has when it declares none.</summary>
    internal const int DefaultInvokedRunLimit = 10;

    /// <summary>
    /// Who may fire each event, start an instance and read one, as the machine declares it. A trigger or read with no
    /// entry declared no rule.
    /// </summary>
    internal MachineRules Rules { get; init; } = MachineRules.None;

    /// <summary>
    /// The triggers with an edge whose guard or reducer is a delegate rather than a declarative rule or reduction,
    /// which the IR and its hash do not describe.
    /// </summary>
    internal IReadOnlySet<TTrigger> DelegateTriggers { get; init; } = new HashSet<TTrigger>();

    /// <summary>The record each trigger declares with <c>WithInput</c>, by trigger. A trigger with none is absent.</summary>
    internal IReadOnlyDictionary<TTrigger, Type> TriggerInputTypes { get; init; } =
        new Dictionary<TTrigger, Type>();

    /// <summary>The machine's declared <c>start</c>, or null when it declares none.</summary>
    internal StartDeclaration? Start { get; init; }

    /// <summary>
    /// The record each state declares its context with <c>Context&lt;TContext&gt;()</c>, by state. A state declared
    /// with <c>Context()</c>, or with no declarative context, is absent.
    /// </summary>
    internal IReadOnlyDictionary<TState, Type> ContextTypes { get; init; } =
        new Dictionary<TState, Type>();

    /// <summary>Every state of <typeparamref name="TState"/>, in declaration order, with its context record.</summary>
    internal IReadOnlyList<MachineState> States =>
        Enum.GetValues<TState>()
            .Select(state => new MachineState(
                state.ToString(),
                ContextTypes.GetValueOrDefault(state)
            ))
            .ToList();

    /// <summary>
    /// Every write to the context only the server makes: invoked trains' outcomes, the effect, and events gated by
    /// <c>Requires</c> or <c>RequiresPolicy</c>.
    /// </summary>
    internal IReadOnlyList<ServerWrite> ServerWrites { get; init; } = [];

    /// <summary>
    /// One sentence per server-written field the declared start reaches; empty when it reaches none, or the machine
    /// declares no start. A client must not start an instance already holding what only the server writes.
    /// </summary>
    internal IReadOnlyList<string> StartProblems()
    {
        if (Start is not { } start)
            return [];
        var reached = start.InputFields;
        if (reached is { Count: 0 })
            return [];

        var problems = new List<string>();
        foreach (var write in ServerWrites)
        {
            if (write.Fields is { Count: 0 })
                continue;
            var overlap =
                reached is null || write.Fields is null
                    ? (reached ?? write.Fields ?? ["(any field)"]).ToList()
                    : reached.Intersect(write.Fields, StringComparer.Ordinal).ToList();
            if (overlap.Count == 0)
                continue;
            problems.Add(
                $"The machine '{Definition.Id}' starts from {start.InputType.Name}, which reaches "
                    + string.Join(
                        ", ",
                        overlap.OrderBy(f => f, StringComparer.Ordinal).Select(f => $"'{f}'")
                    )
                    + $", a field {write.Source} writes. A client could start an instance already holding what "
                    + "only the server writes. Leave the field out of the start's reduction."
            );
        }
        return problems;
    }

    /// <summary>
    /// Every trigger the machine declares, with who may fire it and the CLR type of its input: each member of
    /// <typeparamref name="TTrigger"/> (effect-bound when an edge on it binds the machine's effect, else a client
    /// trigger), then each invoking state's outcome triggers (the success outcome's input is the train's output), then
    /// each timed state's timer trigger, which takes no input.
    /// </summary>
    internal IReadOnlyList<MachineTrigger> Triggers =>
        Enum.GetValues<TTrigger>()
            .Select(trigger => new MachineTrigger(
                trigger.ToString(),
                Effects.Any(e => EqualityComparer<TTrigger>.Default.Equals(e.Trigger, trigger))
                    ? TriggerKind.EffectBound
                    : TriggerKind.Client,
                TriggerInputTypes.GetValueOrDefault(trigger)
            ))
            .Concat(
                Invokes.Values.SelectMany(invoke =>
                    Enum.GetValues<InvokeOutcomeKind>()
                        .Select(kind => new MachineTrigger(
                            invoke.TriggerName(kind),
                            TriggerKind.Outcome,
                            kind == InvokeOutcomeKind.Done ? invoke.OutputType : null
                        ))
                )
            )
            .Concat(
                Timers
                    .Values.OrderBy(t => t.State)
                    .Select(t => new MachineTrigger(t.TriggerName, TriggerKind.Timer, null))
            )
            .ToList();
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
    IMachineBuilder<TState, TTrigger> SystemOwned() =>
        throw new NotSupportedException($"{GetType().Name} does not support SystemOwned.");

    /// <summary>
    /// The most live invoked runs one user may hold for entering one of this machine's invoking states to be allowed:
    /// the user's runs are counted across every machine, and entering when they already hold that many is refused
    /// with <c>invoke-limit-reached</c>, and nothing is written. A run is live from the entry that queued it until it
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
    /// Declare who may watch an instance through <c>machineChanged</c>, under the same floor as
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
    /// discarded, nor is one an effect is running on (<c>discard-not-allowed</c>).
    /// </summary>
    /// <param name="rule">Declares the discard's rule.</param>
    IMachineBuilder<TState, TTrigger> Discard(Action<IReadRuleBuilder> rule) =>
        throw new NotSupportedException($"{GetType().Name} does not support Discard.");
}

/// <summary>
/// Declares a machine's <c>start</c>: how its input record becomes the initial context, and the rule a caller
/// must meet. See <see cref="IMachineBuilder{TState,TTrigger}.Start{TStartInput}"/>.
/// </summary>
/// <typeparam name="TStartInput">The record a client starts an instance with.</typeparam>
public interface IStartBuilder<TStartInput>
{
    /// <summary>Produce the initial context from the start input with a declarative <see cref="Reduction"/>.</summary>
    IStartBuilder<TStartInput> Reduce(Reduction reduce);

    /// <summary>Only a caller in at least one of <paramref name="roles"/> may start an instance.</summary>
    IStartBuilder<TStartInput> Requires(params string[] roles);

    /// <summary>Only a caller who satisfies the authorization policy <paramref name="policy"/> may start an instance.</summary>
    IStartBuilder<TStartInput> RequiresPolicy(string policy);

    /// <summary>Any authenticated caller may start an instance.</summary>
    IStartBuilder<TStartInput> AnyAuthenticated();

    /// <summary>Any caller may start an instance, authenticated or not.</summary>
    IStartBuilder<TStartInput> Anonymous();
}

/// <summary>
/// Declares who may read an instance, through <c>get</c> or <c>machineChanged</c>, or delete one through
/// <c>discard</c>. See <see cref="IMachineBuilder{TState,TTrigger}.Get"/>,
/// <see cref="IMachineBuilder{TState,TTrigger}.Watch"/> and <see cref="IMachineBuilder{TState,TTrigger}.Discard"/>.
/// </summary>
public interface IReadRuleBuilder
{
    /// <summary>Only the instance's owner may read it.</summary>
    IReadRuleBuilder OwnerOnly();

    /// <summary>Only a caller in at least one of <paramref name="roles"/> may read it.</summary>
    IReadRuleBuilder Requires(params string[] roles);

    /// <summary>Only a caller who satisfies the authorization policy <paramref name="policy"/> may read it.</summary>
    IReadRuleBuilder RequiresPolicy(string policy);

    /// <summary>Any authenticated caller may read it.</summary>
    IReadRuleBuilder AnyAuthenticated();

    /// <summary>Any caller may read it, authenticated or not.</summary>
    IReadRuleBuilder Anonymous();
}

/// <summary>
/// A state's timer: where the machine goes once the state has been held for the declared duration. See
/// <see cref="IStateBuilder{TState,TTrigger}.After(TimeSpan)"/>.
/// </summary>
public interface ITimerBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Finish the timer: land in <paramref name="target"/>. Returns the state builder for more configuration.</summary>
    IStateBuilder<TState, TTrigger> GoTo(TState target);
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
    IInvokeBuilder<TState, TTrigger> Invokes<TTrain, TInput, TOutput>(
        Func<JsonObject, TInput> input
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<TInput, TOutput> =>
        throw new NotSupportedException($"{GetType().Name} does not support Invokes.");

    /// <summary>
    /// Leave this state once it has been held for <paramref name="delay"/>: the timer starts on every entry (a
    /// self-transition included) and is cancelled on leaving. Finish it with
    /// <see cref="ITimerBuilder{TState,TTrigger}.GoTo"/>. Only the server fires it, when it is due: a client never sends
    /// the timer trigger (<c>&lt;State&gt;.after</c>), and an advance refuses it as <c>timer-bound</c>. A state has at
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
    /// bounds is refused as <c>invalid-input</c>. Otherwise as <see cref="After(TimeSpan)"/>.
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

/// <summary>A single transition: its guard, message, reducer, optional exactly-once effect, and destination.</summary>
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
    /// anything other than an object is refused as <c>invalid-input</c>. The record's <c>[JsonPropertyName]</c>
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
    /// <c>forbidden</c>. An event has one rule: every edge of it declares the same one, and an event that requires a
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
    private readonly Dictionary<TState, Type> _contextTypes = [];
    private readonly List<EdgeInput> _edgeInputs = [];
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
    private StartDeclaration? _start;
    private readonly List<ServerWrite> _serverWrites = [];
    private readonly List<EventWrite> _eventWrites = [];
    private int _invokedRunLimit = BuiltMachine<TState, TTrigger>.DefaultInvokedRunLimit;
    private readonly List<InvokeDraft> _invokes = [];
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
        _watchRule = new CallerRuleDraft($"The machine '{_id}' machineChanged", read: true);
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
    /// more than one effect with <c>RunsOnce</c>, enters an effect's target state by any other transition, or binds an
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

        RefuseOtherEdgesIntoEffectTargets();
        RefuseEffectsOnInvokingStates();

        var invokes = BuildInvokes();
        RefuseOtherEdgesIntoOutcomeTargets(invokes);
        var timers = BuildTimers(invokes);

        var declaredInputs = DeclaredInputs();
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
            InputSchemas = InputSchemas(declaredInputs),
            Timers = timers,
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
            .ToList();
        // Only a machine that declares rules is reached event by event, so only there is an open event a caller's.
        if (
            rules.Any
            && OpenWritesToServerFields(serverWrites).ToList() is { Count: > 0 } openWrites
        )
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(" ", openWrites.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
            );

        return new BuiltMachine<TState, TTrigger>(definition, _committed, _effects, declarative)
        {
            SystemOwned = _systemOwned,
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
                    .DistinctBy(w => (w.Source, Fields: string.Join(",", w.Fields ?? [])))
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
                    + string.Join(
                        ", ",
                        overlap.OrderBy(f => f, StringComparer.Ordinal).Select(f => $"'{f}'")
                    )
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
            if (edges.All(e => e.Rule is null))
                continue;
            var first = edges.First(e => e.Rule is not null).Rule!;
            if (edges.Any(e => e.Rule is null || !e.Rule.SameAs(first)))
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
                && EqualityComparer<TState>.Default.Equals(e.To, _initial!.Value)
                && !EqualityComparer<TState>.Default.Equals(e.From, e.To)
                && !_invokes.Any(i => EqualityComparer<TState>.Default.Equals(i.State, e.To))
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
                    + string.Join(" ", problems.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
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

        // The success outcome's input is the train's output, validated against its type's schema like a trigger's
        // input, so the output type needs one.
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
            {
                OutputSchema = outputSchemas[d],
            }
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
                EqualityComparer<TState>.Default.Equals(target, _initial!.Value)
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
            if (group.Count() > 1)
                problems.Add(
                    $"the state {group.Key} declares {(group.Count() == 2 ? "two" : group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                        + "timers, but a state has at most one. Keep one After(...) on it."
                );

        var built = new Dictionary<TState, TimerDefinition<TState>>();
        foreach (var draft in _timers)
        {
            var state = draft.State;
            if (draft.Target is not { } target)
            {
                problems.Add(
                    $"the timer of {state} has no target. Finish After(...) with `.GoTo({typeof(TState).Name}.X)`."
                );
                continue;
            }
            if (EqualityComparer<TState>.Default.Equals(target, state))
            {
                problems.Add(
                    $"the timer of {state} goes back to {state}, so it would fire again every time it fired. Send it "
                        + "to another state, or restart the wait with an event that enters the state."
                );
                continue;
            }
            if (
                _effects.FirstOrDefault(e =>
                    EqualityComparer<TState>.Default.Equals(e.To, target)
                ) is
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

            TimerField? field = null;
            Func<JsonObject, TimeSpan?>? computed = null;
            if (draft.Fixed is { } fixedDuration)
            {
                if (fixedDuration <= TimeSpan.Zero)
                    problems.Add(
                        $"the timer of {state} waits {fixedDuration}, but a timer waits a positive time."
                    );
                else if (fixedDuration > MachineTimers.MaxDuration)
                    problems.Add(
                        $"the timer of {state} waits {fixedDuration}, longer than the {MachineTimers.MaxDuration.TotalDays} "
                            + "days a timer may wait (MachineTimers.MaxDuration)."
                    );
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
                            + $"{MachineTimers.MaxDuration.TotalDays} days a timer may wait (MachineTimers.MaxDuration)."
                    );
                (field, computed) = TimerDuration(draft, problems);
            }

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

        problems.AddRange(TimerCycles(built));

        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"The machine '{_id}' cannot be built: "
                    + string.Join(
                        " ",
                        problems.Distinct().Select(p => char.ToUpperInvariant(p[0]) + p[1..])
                    )
            );
        return built;
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
        var selector = draft.Duration!;
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
                .FirstOrDefault(p => MemberPath.JsonNameOf(p) == name);
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
                    $"the timer of {state} reads its duration from '{name}', which {declared!.Name} declares as a "
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
        property
            .GetCustomAttributes(typeof(Trax.Effect.Attributes.TraxSensitiveAttribute), true)
            .Length > 0;

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

    // How a computed duration reads the context: the camelCase names the schema reflects, and a TimeSpan written as an
    // ISO-8601 duration, as a context carries it.
    private static readonly JsonSerializerOptions TimerContextJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new IsoDurationJsonConverter(),
        },
    };

    // Each state has at most one timer, so following timers from any state either ends or comes round to a state it
    // passed: a cycle no event can break. Each cycle is named once, from its first state in declaration order.
    private static IEnumerable<string> TimerCycles(
        IReadOnlyDictionary<TState, TimerDefinition<TState>> timers
    )
    {
        var named = new HashSet<TState>();
        foreach (var start in Enum.GetValues<TState>())
        {
            var path = new List<TState>();
            var current = start;
            while (timers.TryGetValue(current, out var timer) && !path.Contains(current))
            {
                path.Add(current);
                current = timer.Target;
            }
            if (!path.Contains(current) || named.Contains(current))
                continue;
            var cycle = path.SkipWhile(s => !EqualityComparer<TState>.Default.Equals(s, current))
                .ToList();
            var first = cycle.MinBy(s => Array.IndexOf(Enum.GetValues<TState>(), s));
            var at = cycle.IndexOf(first);
            var ordered = cycle.Skip(at).Concat(cycle.Take(at)).Append(first).ToList();
            foreach (var s in cycle)
                named.Add(s);
            yield return $"the timers of {string.Join(" -> ", ordered)} form a cycle no event breaks, so the "
                + "instance would go round it forever. Let an event, not a timer, lead out of one of these states.";
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
                context => input(context)
            );
            owner._invokes.Add(draft);
            return new InvokeBuilder(owner, this, draft);
        }

        public ITimerBuilder<TState, TTrigger> After(TimeSpan delay)
        {
            var draft = new TimerDraft(state, delay, null, delay, delay);
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
            var draft = new TimerDraft(state, null, duration, min, max);
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
        Func<JsonObject, object?> CreateInput
    )
    {
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
    ) : ITransitionBuilder<TState, TTrigger>
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
            return state;
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
