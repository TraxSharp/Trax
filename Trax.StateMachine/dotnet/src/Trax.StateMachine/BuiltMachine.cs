using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

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

    /// <summary>
    /// Finds the events that take <paramref name="snapshot"/> to the state <paramref name="target"/> with the page's
    /// fields applied, as <see cref="IrMachine.Plan"/> does for the machine built from this one's IR. Every custom rule
    /// of a compiled machine has its handler, so it never answers <see cref="PlanResult.Undetermined"/>. Never throws.
    /// </summary>
    /// <param name="snapshot">Where the server is.</param>
    /// <param name="target">The state the page is in.</param>
    /// <param name="fields">The event that writes the page's fields, or null when there is none.</param>
    /// <param name="navigation">The triggers, without input, that move between states.</param>
    /// <param name="maxDepth">The longest plan to look for; twice the machine's states when null.</param>
    public PlanResult Plan(
        Snapshot snapshot,
        string target,
        PlannedEvent? fields,
        IReadOnlyList<string> navigation,
        int? maxDepth = null
    )
    {
        var states = Enum.GetNames<TState>().ToHashSet(StringComparer.Ordinal);
        // A trigger no transition fires on is not one to move with, as in every engine's planner.
        var fired = Definition
            .Transitions.Select(t => t.Trigger.ToString()!)
            .ToHashSet(StringComparer.Ordinal);
        var clientTriggers = Triggers
            .Where(t => t.Kind == TriggerKind.Client && fired.Contains(t.Name))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
        return EventPlanner.Plan(
            new PlanGraph(
                states.Count,
                states.Contains,
                clientTriggers.Contains,
                static (_, _) => true,
                Engine.Advance,
                Engine.Serialize
            ),
            snapshot,
            target,
            fields,
            navigation,
            maxDepth
        );
    }

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
    /// The triggers only the server fires (a policy reacting to another machine), never served to a client: they are
    /// not in <see cref="Triggers"/>, which the typed API and the startup check are built from, and a client sending
    /// one is refused, since it has no rule. Declared through <see cref="IIrMachineBuilder{TState,TTrigger}"/>.
    /// </summary>
    internal IReadOnlySet<TTrigger> ServerOnly { get; init; } = new HashSet<TTrigger>();

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
    /// Whether the machine binds a custom guard or reducer by name (<c>CustomGuard</c>, <c>CustomReducer</c>), a
    /// handler only the server has.
    /// </summary>
    internal bool BindsCustomHandlers { get; init; }

    /// <summary>
    /// Whether any edge decides or computes something on the server: a guard (declarative or a delegate), or a reducer
    /// other than <see cref="Reduction.Keep"/>. A saved snapshot would step around either.
    /// </summary>
    internal bool HasEdgeLogic
    {
        get
        {
            var declared = Declarative?.Transitions;
            var paired = declared is not null && declared.Count == Definition.Transitions.Count;
            for (var i = 0; i < Definition.Transitions.Count; i++)
            {
                var edge = Definition.Transitions[i];
                if (edge.Guard is not null)
                    return true;
                if (edge.Reduce is null)
                    continue;
                if (!paired || declared![i].Reduce is not Reduction.Keep)
                    return true;
            }
            return false;
        }
    }

    /// <summary>Whether the declared start copies or computes anything from its input into the initial context.</summary>
    internal bool StartReducesInput => Start is { } start && start.InputFields is not { Count: 0 };

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
                    + string.Join(", ", overlap.Order(StringComparer.Ordinal).Select(f => $"'{f}'"))
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
            .Where(trigger => !ServerOnly.Contains(trigger))
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
                    .Select(t => new MachineTrigger(
                        t.TriggerName,
                        TriggerKind.Timer,
                        InputType: null
                    ))
            )
            .ToList();
}
