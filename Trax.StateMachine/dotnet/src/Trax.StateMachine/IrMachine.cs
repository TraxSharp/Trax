using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// A machine built from its IR (the <c>machine.ir.json</c> a host exports) rather than from C# code: the engine a
/// client predicts with when it has the host's manifest but not the machine's assembly, as every other language's
/// client builds its machine from the IR. States and triggers are the IR's names, contexts and inputs are JSON.
/// </summary>
/// <remarks>
/// <para>It answers as <see cref="SnapshotMachine{TState,TTrigger}"/> does for the machine that exported the IR, case for
/// case: the conformance kit (<c>Trax.StateMachine/conformance/</c>) holds the two to the same codes and wire bytes.
/// Guards, reducers and validators are the IR's declarative rules, run by <see cref="RuleEvaluator"/>,
/// <see cref="ReductionEvaluator"/> and the schema validator.</para>
/// <para>A custom rule or reduction is bound by name only. Without a handler a custom guard does not hold and a custom
/// reduction keeps the context, which need not be the server's answer; <see cref="Rehydrate(string, bool)"/> with
/// <c>fromServer</c> counts a custom state requirement it has no handler for as passed, since the server checked it.</para>
/// <para>Every operation is total, as on the generic engine. Building one from an IR that is not one throws.</para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-machine">SDK reference</seealso>
public sealed class IrMachine
{
    private const string GuardRejected = "A state requirement was not satisfied.";

    private static readonly ContextSchema ReceiptInput = new([
        new FieldSchema("receipt", JsonFieldType.String, Nullable: true, []),
    ]);

    private readonly IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>> _guards;
    private readonly IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>> _serverGuards;
    private readonly IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, JsonObject>> _reducers;
    private readonly JsonObject? _initialContext;
    private readonly HashSet<string> _states;
    private readonly HashSet<string> _triggers;
    private readonly List<Edge> _transitions;
    private readonly Dictionary<string, ContextSchema> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Outcome> _outcomes;
    private readonly Dictionary<string, Timer> _timers;
    private readonly Dictionary<string, ContextSchema> _contexts;
    private readonly Dictionary<string, Rule> _invariants;
    private readonly HashSet<(string State, string Trigger)> _undetermined;

    private sealed record Edge(
        string From,
        string Trigger,
        string To,
        Rule? Guard,
        string? GuardMessage,
        Reduction? Reduce
    );

    private sealed record Outcome(
        string State,
        string Kind,
        IReadOnlyList<Edge> Edges,
        ContextSchema Output
    );

    private sealed record Timer(
        string State,
        string Trigger,
        string Target,
        TimeSpan? After,
        string? Field,
        TimeSpan Min,
        TimeSpan Max
    );

    /// <summary>Builds the machine an IR document describes.</summary>
    /// <param name="ir">The IR, as the host exported it (its <c>differential</c> block, if any, is ignored).</param>
    /// <param name="customGuards">Handlers for the custom rules the IR names, by name. Optional.</param>
    /// <param name="customReducers">Handlers for the custom reductions the IR names, by name. Optional.</param>
    /// <exception cref="ArgumentException">The document is not an IR this engine can read.</exception>
    public IrMachine(
        JsonObject ir,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>>? customGuards = null,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, JsonObject>>? customReducers = null
    )
    {
        ArgumentNullException.ThrowIfNull(ir);
        _guards = customGuards ?? new Dictionary<string, Func<JsonObject, JsonNode?, bool>>();
        _reducers =
            customReducers ?? new Dictionary<string, Func<JsonObject, JsonNode?, JsonObject>>();
        try
        {
            // A twin records the IR format it was generated for; an IR of a newer format than this engine reads is refused.
            if (
                ir["irFormat"] is JsonValue format
                && format.GetValue<int>() > IrExporter.FormatVersion
            )
                throw new ArgumentException(
                    $"The IR is in format {format.GetValue<int>()}, and this engine reads format {IrExporter.FormatVersion}. Update Trax.StateMachine.",
                    nameof(ir)
                );
            Id = Text(ir["id"], "id");
            Version = ir["version"]!.GetValue<int>();
            InitialState = Text(ir["initialState"], "initialState");
            _initialContext = ir["initialContext"] as JsonObject;
            _states = Names(ir["states"], "states");
            _triggers = Names(ir["triggers"], "triggers");
            CommittedStates = Names(ir["committedStates"], "committedStates");
            _contexts = Map(ir["context"], ReadSchema);
            _invariants = Map(ir["invariants"], ReadRule);
            foreach (var (trigger, schema) in Map(ir["inputs"], ReadSchema))
                _inputs[trigger] = schema;

            var transitions = (ir["transitions"] as JsonArray ?? [])
                .Select(t => t!.AsObject())
                .ToList();
            _transitions = transitions
                .Select(t => ReadEdge(t, Text(t["from"], "from"), Text(t["trigger"], "trigger")))
                .ToList();
            EffectTriggers = transitions
                .Where(t => t["effect"] is JsonObject)
                .Select(t => t["trigger"]!.GetValue<string>())
                .ToHashSet(StringComparer.Ordinal);
            // Every trigger's input is checked against one schema before a guard reads it: the record it declares, else
            // nothing, except that an effect-bound trigger takes the receipt the effect runner hands it.
            foreach (var trigger in _triggers)
                if (!_inputs.ContainsKey(trigger))
                    _inputs[trigger] = EffectTriggers.Contains(trigger)
                        ? ReceiptInput
                        : ContextSchema.Empty;

            _outcomes = new Dictionary<string, Outcome>(StringComparer.Ordinal);
            foreach (var (name, node) in ir["outcomes"] as JsonObject ?? [])
            {
                var o = node!.AsObject();
                var state = Text(o["state"], "outcomes.state");
                var kind = Text(o["outcome"], "outcomes.outcome");
                var edges = (o["edges"] as JsonArray ?? [])
                    .Select(e => ReadEdge(e!.AsObject(), state, name))
                    .ToList();
                var output =
                    kind == "done" && o["output"] is JsonObject schema
                        ? ReadSchema(schema)
                        : ContextSchema.Empty;
                _outcomes[name] = new Outcome(state, kind, edges, output);
            }

            _timers = new Dictionary<string, Timer>(StringComparer.Ordinal);
            foreach (var node in ir["timers"] as JsonArray ?? [])
            {
                var t = node!.AsObject();
                var field = t["field"] as JsonObject;
                _timers[Text(t["state"], "timers.state")] = new Timer(
                    Text(t["state"], "timers.state"),
                    Text(t["trigger"], "timers.trigger"),
                    Text(t["target"], "timers.target"),
                    t["after"] is { } after ? Duration(after) : null,
                    field is null ? null : Text(field["name"], "timers.field.name"),
                    field is null ? TimeSpan.Zero : Duration(field["min"]),
                    field is null ? TimeSpan.Zero : Duration(field["max"])
                );
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException(
                $"The document is not a machine IR this engine can read: {ex.Message}",
                nameof(ir),
                ex
            );
        }

        // On a snapshot the server sent, a custom state requirement with no handler here counts as passed.
        var trusted = new Dictionary<string, Func<JsonObject, JsonNode?, bool>>(
            _guards,
            StringComparer.Ordinal
        );
        foreach (var rule in _invariants.Values)
            TrustUnhandled(rule, trusted);
        _serverGuards = trusted;

        _undetermined = _transitions
            .Where(t =>
                NamesUnhandled(t.Guard)
                || (t.Reduce is Reduction.Custom r && !_reducers.ContainsKey(r.Name))
                || NamesUnhandled(_invariants.GetValueOrDefault(t.To))
            )
            .Select(t => (t.From, t.Trigger))
            .ToHashSet();
    }

    /// <summary>The machine's id (<c>checkout</c>).</summary>
    public string Id { get; }

    /// <summary>The definition version its snapshots carry.</summary>
    public int Version { get; }

    /// <summary>The state a new instance starts in.</summary>
    public string InitialState { get; }

    /// <summary>The states marked <c>Committed()</c>: a soft autosave may not move a draft out of them.</summary>
    public IReadOnlySet<string> CommittedStates { get; }

    /// <summary>The triggers whose edge runs the machine's effect: a client sends them with no input and no prediction.</summary>
    public IReadOnlySet<string> EffectTriggers { get; }

    /// <summary>The states with a timer.</summary>
    public IReadOnlySet<string> TimedStates => _timers.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The snapshot a new instance begins with.</summary>
    public Snapshot CreateInitialSnapshot() =>
        new()
        {
            Machine = Id,
            Version = Version,
            State = InitialState,
            Context = InitialContext(),
        };

    /// <summary>
    /// Applies a client's trigger, as <see cref="SnapshotMachine{TState,TTrigger}.Advance"/> does: the same refusals in the
    /// same order. Never throws.
    /// </summary>
    public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_states.Contains(snapshot.State))
            return Rejected(RefusalCode.NoTransition, $"Unknown state '{snapshot.State}'.");
        if (_outcomes.ContainsKey(trigger))
            return Rejected(
                RefusalCode.OutcomeBound,
                "This step is completed by the outcome of its work, not by an action."
            );
        if (_timers.Values.Any(t => t.Trigger == trigger))
            return Rejected(
                RefusalCode.TimerBound,
                "This step moves on by itself when its time is up, not by an action."
            );
        if (!_triggers.Contains(trigger))
            return Rejected(RefusalCode.NoTransition, $"Unknown trigger '{trigger}'.");

        var matches = _transitions
            .Where(t => t.From == snapshot.State && t.Trigger == trigger)
            .ToList();
        if (matches.Count == 0)
            return Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}'."
            );
        if (SchemaValidator.ValidateInput(_inputs[trigger], input) is { } inputProblem)
            return Rejected(RefusalCode.InvalidInput, inputProblem);

        return Guarded(() =>
        {
            var chosen = matches.FirstOrDefault(t => Passes(t.Guard, snapshot.Context, input));
            if (chosen is null)
                return Rejected(
                    RefusalCode.GuardFailed,
                    matches.Select(m => m.GuardMessage).FirstOrDefault(m => m is not null)
                        ?? $"A transition from '{snapshot.State}' on '{trigger}' exists but its guard rejected the input."
                );
            return Enter(chosen.To, Reduce(chosen.Reduce, snapshot.Context, input));
        });
    }

    /// <summary>
    /// Applies an invoked train's outcome named by its trigger (<c>Fetching.done</c>) with the train's output as
    /// <paramref name="input"/>: the first edge whose guard holds for the output is taken. The server applies outcomes;
    /// a client uses this only to replay one. Never throws.
    /// </summary>
    public AdvanceResult ApplyOutcome(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!OutcomeTriggers.TryParse(trigger, out var triggerState, out _))
            return Rejected(RefusalCode.NoTransition, $"Unknown trigger '{trigger}'.");
        if (!_states.Contains(snapshot.State))
            return Rejected(RefusalCode.NoTransition, $"Unknown state '{snapshot.State}'.");
        if (triggerState != snapshot.State)
            return Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}'."
            );
        if (!_outcomes.TryGetValue(trigger, out var outcome))
            return Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}': the state invokes no train."
            );
        if (SchemaValidator.ValidateInput(outcome.Output, input) is { } outputProblem)
            return Rejected(RefusalCode.InvalidInput, outputProblem);

        return Guarded(() =>
        {
            var chosen = outcome.Edges.FirstOrDefault(e =>
                Passes(e.Guard, snapshot.Context, input)
            );
            if (chosen is null)
                return Rejected(
                    RefusalCode.NoTransition,
                    $"No OnDone edge from '{snapshot.State}' accepts the train's output."
                );
            return Enter(chosen.To, Reduce(chosen.Reduce, snapshot.Context, input));
        });
    }

    /// <summary>
    /// Fires the timer of the state the snapshot is in (<c>Review.after</c>), carrying the context to the timer's target.
    /// The server fires timers; a client uses this only to replay one. Never throws.
    /// </summary>
    public AdvanceResult ApplyTimer(Snapshot snapshot, string trigger)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_states.Contains(snapshot.State))
            return Rejected(RefusalCode.NoTransition, $"Unknown state '{snapshot.State}'.");
        if (!_timers.TryGetValue(snapshot.State, out var timer) || timer.Trigger != trigger)
            return Rejected(
                RefusalCode.NoTransition,
                $"No timer fires from '{snapshot.State}' on '{trigger}'."
            );
        return Guarded(() => Enter(timer.Target, (JsonObject)snapshot.Context.DeepClone()));
    }

    /// <summary>Reads stored or sent JSON as a snapshot of this machine, as the generic engine does. Never throws.</summary>
    public RehydrationResult Rehydrate(string json) => Rehydrate(json, fromServer: false);

    /// <summary>
    /// Reads JSON as a snapshot of this machine. With <paramref name="fromServer"/>, a custom state requirement this
    /// machine has no handler for counts as passed: the server applied the transition and checked it. Never throws.
    /// </summary>
    public RehydrationResult Rehydrate(string json, bool fromServer)
    {
        try
        {
            return RehydrateCore(json, fromServer ? _serverGuards : _guards);
        }
        catch (Exception ex)
        {
            return new RehydrationResult.Error(
                RefusalCode.Malformed,
                "The snapshot could not be read."
            )
            {
                Exception = ex,
            };
        }
    }

    /// <summary>The snapshot as canonical wire (RFC 8785), byte for byte what every engine writes.</summary>
    public string Serialize(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CanonicalJson.SerializeSnapshot(
            snapshot.Machine,
            snapshot.Version,
            snapshot.State,
            snapshot.Context
        );
    }

    /// <summary>Whether a client's <paramref name="trigger"/> fires from the snapshot with <paramref name="input"/>. Never throws.</summary>
    public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_states.Contains(snapshot.State) || !_triggers.Contains(trigger))
            return false;
        if (SchemaValidator.ValidateInput(_inputs[trigger], input) is not null)
            return false;
        try
        {
            return _transitions.Any(t =>
                t.From == snapshot.State
                && t.Trigger == trigger
                && Passes(t.Guard, snapshot.Context, input)
            );
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The distinct client triggers with an edge out of the snapshot's state. Never throws.</summary>
    public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _transitions
            .Where(t => t.From == snapshot.State)
            .Select(t => t.Trigger)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Finds the events that take <paramref name="snapshot"/> to the state <paramref name="target"/> with the page's
    /// fields applied: what a client sends to bring the server to where a page that ran ahead of it is. Breadth first,
    /// trying the fields event and then <paramref name="navigation"/> in the order given from each snapshot, so every
    /// engine answers the same plan. An edge that rests on a custom rule this machine holds no handler for is not tried,
    /// and answers <see cref="PlanResult.Undetermined"/> when nothing else reaches the target. Never throws.
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
    ) =>
        EventPlanner.Plan(
            new PlanGraph(
                _states.Count,
                _states.Contains,
                Navigable,
                Determines,
                Advance,
                Serialize
            ),
            snapshot,
            target,
            fields,
            navigation,
            maxDepth
        );

    // Whether every edge out of `state` on `trigger` can be evaluated here: none rests on a custom guard, a custom
    // reduction, or a custom requirement of the state it enters, that this machine holds no handler for. A client sends
    // an event on such an edge with no prediction.
    internal bool Determines(string state, string trigger) =>
        !_undetermined.Contains((state, trigger));

    // A trigger a client may move with: one the machine has, not bound to the effect, an outcome or a timer.
    private bool Navigable(string trigger) =>
        _triggers.Contains(trigger)
        && !EffectTriggers.Contains(trigger)
        && !_outcomes.ContainsKey(trigger)
        && !_timers.Values.Any(t => t.Trigger == trigger);

    private bool NamesUnhandled(Rule? rule) =>
        rule switch
        {
            Rule.Custom c => !_guards.ContainsKey(c.Name),
            Rule.All a => a.Rules.Any(NamesUnhandled),
            Rule.Any a => a.Rules.Any(NamesUnhandled),
            _ => false,
        };

    // ── The engine ─────────────────────────────────────────────────────────────────────────────

    private RehydrationResult RehydrateCore(
        string json,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>> guards
    )
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return new RehydrationResult.Error(RefusalCode.Malformed, "Input is not valid JSON.");
        }
        if (node is not JsonObject obj)
            return new RehydrationResult.Error(
                RefusalCode.Malformed,
                "Snapshot must be a JSON object."
            );

        var machine = AsString(obj["machine"]);
        var state = AsString(obj["state"]);
        var version = AsInt(obj["version"]);
        if (
            machine is null
            || state is null
            || version is null
            || obj["context"] is not JsonObject context
        )
            return new RehydrationResult.Error(
                RefusalCode.Malformed,
                "Snapshot is missing required fields (machine, version, state, context)."
            );
        if (machine != Id)
            return new RehydrationResult.Error(
                RefusalCode.UnknownMachine,
                $"Snapshot machine '{machine}' does not match '{Id}'."
            );
        // An IR carries no migrations, so a snapshot of another version is refused rather than misread.
        if (version.Value > Version)
            return new RehydrationResult.Error(
                RefusalCode.VersionMismatch,
                $"Snapshot version {version.Value} is newer than definition version {Version}."
            );
        if (version.Value < Version)
            return new RehydrationResult.Error(
                RefusalCode.VersionMismatch,
                $"No migration from version {version.Value} to {Version}."
            );
        if (!_states.Contains(state))
            return new RehydrationResult.Error(
                RefusalCode.UnknownState,
                $"Unknown state '{state}'."
            );

        var copy = (JsonObject)context.DeepClone();
        if (StorableJson.Problem(copy) is { } unstorable)
            return new RehydrationResult.Error(RefusalCode.Malformed, unstorable);
        if (ValidateContext(state, copy, guards) is { } contextError)
            return new RehydrationResult.Error(RefusalCode.InvalidContext, contextError);

        return new RehydrationResult.Ok(
            new Snapshot
            {
                Machine = machine,
                Version = Version,
                State = state,
                Context = copy,
            }
        );
    }

    // The successor of a transition into `state`: the target's timer reads its duration first, so a duration the context
    // does not hold is refused as input before the context is validated; then the target's validator.
    private AdvanceResult Enter(string state, JsonObject context)
    {
        if (TimerProblem(state, context) is { } timerProblem)
            return Rejected(RefusalCode.InvalidInput, timerProblem);
        if (ValidateContext(state, context, _guards) is { } contextError)
            return Rejected(RefusalCode.InvalidContext, contextError);
        return new AdvanceResult.Transitioned(
            new Snapshot
            {
                Machine = Id,
                Version = Version,
                State = state,
                Context = context,
            }
        );
    }

    private string? TimerProblem(string state, JsonObject context)
    {
        if (!_timers.TryGetValue(state, out var timer) || timer.After is not null)
            return null;
        if (
            context[timer.Field!] is not JsonValue value
            || value.GetValueKind() != JsonValueKind.String
            || !IsoDuration.TryParse(value.GetValue<string>(), out var duration)
        )
            return $"The timer of {state} reads '{timer.Field}', which must hold an ISO-8601 duration such as \"PT5M\".";
        if (duration < timer.Min || duration > timer.Max)
            return $"The timer of {state} waits {IsoDuration.Format(duration)}, outside its bounds of "
                + $"{IsoDuration.Format(timer.Min)} to {IsoDuration.Format(timer.Max)}.";
        return null;
    }

    private string? ValidateContext(
        string state,
        JsonObject context,
        IReadOnlyDictionary<string, Func<JsonObject, JsonNode?, bool>> guards
    )
    {
        if (
            _contexts.TryGetValue(state, out var schema)
            && SchemaValidator.Validate(schema, context) is { } error
        )
            return error;
        if (
            _invariants.TryGetValue(state, out var invariant)
            && !RuleEvaluator.Evaluate(invariant, context, null, guards)
        )
            return GuardRejected;
        return null;
    }

    private bool Passes(Rule? guard, JsonObject context, JsonNode? input) =>
        guard is null || RuleEvaluator.Evaluate(guard, context, input, _guards);

    private JsonObject Reduce(Reduction? reduction, JsonObject context, JsonNode? input) =>
        reduction is null
            ? (JsonObject)context.DeepClone()
            : ReductionEvaluator.Apply(reduction, context, input, InitialContext(), _reducers);

    // The IR's initial context, or one derived from the initial state's schema when it carries none.
    private JsonObject InitialContext()
    {
        if (_initialContext is not null)
            return (JsonObject)_initialContext.DeepClone();
        var context = new JsonObject();
        foreach (var field in _contexts.GetValueOrDefault(InitialState)?.Fields ?? [])
            context[field.Name] = field.Nullable
                ? null
                : field.Type switch
                {
                    JsonFieldType.Array => new JsonArray(),
                    JsonFieldType.Number or JsonFieldType.Integer => 0,
                    JsonFieldType.Boolean => false,
                    JsonFieldType.String => field.Enum?.FirstOrDefault() ?? "",
                    _ => new JsonObject(),
                };
        return context;
    }

    private static AdvanceResult Guarded(Func<AdvanceResult> step)
    {
        try
        {
            return step();
        }
        catch (Exception ex)
        {
            return new AdvanceResult.Rejected(
                RefusalCode.InternalError,
                "The transition failed with an unexpected error."
            )
            {
                Exception = ex,
            };
        }
    }

    private static AdvanceResult Rejected(RefusalCode code, string detail) =>
        new AdvanceResult.Rejected(code, detail);

    private static void TrustUnhandled(
        Rule rule,
        Dictionary<string, Func<JsonObject, JsonNode?, bool>> guards
    )
    {
        switch (rule)
        {
            case Rule.Custom c:
                guards.TryAdd(c.Name, (_, _) => true);
                break;
            case Rule.All a:
                foreach (var r in a.Rules)
                    TrustUnhandled(r, guards);
                break;
            case Rule.Any a:
                foreach (var r in a.Rules)
                    TrustUnhandled(r, guards);
                break;
        }
    }

    // ── Reading the IR ─────────────────────────────────────────────────────────────────────────

    private Edge ReadEdge(JsonObject t, string from, string trigger) =>
        new(
            from,
            trigger,
            Text(t["to"], "to"),
            t["guard"] is JsonObject guard ? ReadRule(guard) : null,
            t["guardMessage"] is JsonValue message ? message.GetValue<string>() : null,
            t["reduce"] is JsonObject reduce ? ReadReduction(reduce) : null
        );

    private static Dictionary<string, T> Map<T>(JsonNode? node, Func<JsonObject, T> read)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (key, value) in node as JsonObject ?? [])
            map[key] = read(value!.AsObject());
        return map;
    }

    private static HashSet<string> Names(JsonNode? node, string what) =>
        (node as JsonArray ?? throw new FormatException($"'{what}' is not a list."))
            .Select(n => Text(n, what))
            .ToHashSet(StringComparer.Ordinal);

    private static string Text(JsonNode? node, string what) =>
        node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : throw new FormatException($"'{what}' is not a string.");

    // Names an engine that keeps a context as a plain object cannot hold as a field: assigning one would reach the
    // object's prototype rather than a member. Every engine refuses them, in any segment of a path, so the three agree.
    private static readonly HashSet<string> PrototypeNames = new(StringComparer.Ordinal)
    {
        "__proto__",
        "constructor",
        "prototype",
    };

    private static string FieldName(JsonNode? node, string what)
    {
        var name = Text(node, what);
        if (name.Split('.').Any(PrototypeNames.Contains))
            throw new FormatException($"'{name}' is not a field name any engine holds.");
        return name;
    }

    private static TimeSpan Duration(JsonNode? node) =>
        IsoDuration.TryParse(Text(node, "duration"), out var duration)
            ? duration
            : throw new FormatException($"'{node}' is not an ISO-8601 duration.");

    private static ContextSchema ReadSchema(JsonObject schema) =>
        new(ReadFields(schema["fields"] as JsonArray ?? []));

    private static List<FieldSchema> ReadFields(JsonArray fields) =>
        fields.Select(f => ReadField(f!.AsObject(), FieldName(f!["name"], "field name"))).ToList();

    private static FieldSchema ReadField(JsonObject f, string name) =>
        new FieldSchema(
            name,
            FieldType(Text(f["type"], "type")),
            f["nullable"]?.GetValue<bool>() ?? false,
            (f["constraints"] as JsonArray ?? []).Select(c => ReadRule(c!.AsObject())).ToList()
        )
        {
            Minimum = f["minimum"]?.GetValue<double>(),
            Maximum = f["maximum"]?.GetValue<double>(),
            Enum = (f["enum"] as JsonArray)?.Select(n => Text(n, "enum")).ToList(),
            EnumExact = (f["enumExact"] as JsonArray)?.Select(n => Text(n, "enumExact")).ToList(),
            Format = f["format"] is JsonValue format ? format.GetValue<string>() : null,
            Fields = f["fields"] is JsonArray nested ? ReadFields(nested) : null,
            Values = f["values"] is JsonObject values ? ReadField(values, "") : null,
            Keys = f["keys"] is JsonObject keys ? ReadField(keys, "") : null,
        };

    private static Rule ReadRule(JsonObject r)
    {
        var kind = Text(r["rule"], "rule");
        switch (kind)
        {
            case "all":
                return new Rule.All(ReadRules(r["rules"]));
            case "any":
                return new Rule.Any(ReadRules(r["rules"]));
            case "custom":
                return new Rule.Custom(Text(r["name"], "name"));
        }
        var source = Text(r["source"], "source") switch
        {
            "context" => RuleSource.Context,
            "input" => RuleSource.Input,
            var other => throw new FormatException($"'{other}' is not a rule source."),
        };
        var field = FieldName(r["field"], "field");
        return kind switch
        {
            "present" => new Rule.Present(source, field),
            "absent" => new Rule.Absent(source, field),
            "nonEmpty" => new Rule.NonEmpty(source, field),
            "ofType" => new Rule.OfType(source, field, FieldType(Text(r["type"], "type"))),
            "oneOf" => new Rule.OneOf(
                source,
                field,
                (r["values"] as JsonArray ?? []).Select(v => Text(v, "values")).ToList()
            ),
            "compare" => new Rule.Compare(
                source,
                field,
                Op(r["op"]),
                r["value"]!.GetValue<double>()
            ),
            "count" => new Rule.Count(source, field, Op(r["op"]), r["value"]!.GetValue<int>()),
            "length" => new Rule.Length(source, field, Op(r["op"]), r["value"]!.GetValue<int>()),
            "boolEquals" => new Rule.BoolEquals(source, field, r["value"]!.GetValue<bool>()),
            "arrayOf" => new Rule.ArrayOf(source, field, FieldType(Text(r["type"], "type"))),
            _ => throw new FormatException($"'{kind}' is not a rule."),
        };
    }

    private static List<Rule> ReadRules(JsonNode? node) =>
        (node as JsonArray ?? []).Select(r => ReadRule(r!.AsObject())).ToList();

    private static Reduction ReadReduction(JsonObject r) =>
        Text(r["reduce"], "reduce") switch
        {
            "keep" => new Reduction.Keep(),
            "clear" => new Reduction.Clear(),
            "reset" => new Reduction.Reset(),
            "custom" => new Reduction.Custom(Text(r["name"], "name")),
            "set" => new Reduction.Set(
                (r["steps"] as JsonArray ?? [])
                    .Select(s => new SetStep(
                        FieldName(s!["field"], "field"),
                        ReadSource(s["value"]!.AsObject())
                    ))
                    .ToList()
            ),
            var other => throw new FormatException($"'{other}' is not a reduction."),
        };

    private static ValueSource ReadSource(JsonObject value) =>
        value.ContainsKey("input")
            ? new ValueSource.FromInput(FieldName(value["input"], "input"))
            : new ValueSource.Constant(value["const"]?.DeepClone());

    private static JsonFieldType FieldType(string type) =>
        type switch
        {
            "string" => JsonFieldType.String,
            "number" => JsonFieldType.Number,
            "integer" => JsonFieldType.Integer,
            "boolean" => JsonFieldType.Boolean,
            "array" => JsonFieldType.Array,
            "object" => JsonFieldType.Object,
            _ => throw new FormatException($"'{type}' is not a field type."),
        };

    private static CompareOp Op(JsonNode? node) =>
        Text(node, "op") switch
        {
            "gt" => CompareOp.GreaterThan,
            "gte" => CompareOp.GreaterOrEqual,
            "lt" => CompareOp.LessThan,
            "lte" => CompareOp.LessOrEqual,
            "eq" => CompareOp.EqualTo,
            var other => throw new FormatException($"'{other}' is not a comparison."),
        };

    private static string? AsString(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    // Any integral JSON number, as the generic engine and the TypeScript twin read a version.
    private static int? AsInt(JsonNode? node)
    {
        if (
            node?.GetValueKind() != JsonValueKind.Number
            || !node.AsValue().TryGetValue<double>(out var d)
        )
            return null;
        if (!double.IsInteger(d) || d < int.MinValue || d > int.MaxValue)
            return null;
        return (int)d;
    }
}
