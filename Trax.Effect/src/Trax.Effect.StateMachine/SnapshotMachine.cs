using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Interprets a <see cref="MachineDefinition{TState,TTrigger}"/> over <see cref="Snapshot"/>s. It is
/// the C# half of the cross-language contract; a TypeScript reducer is the other half, and the shared
/// conformance fixtures prove they agree.
///
/// <para><b>A plain reducer, like the TypeScript twin.</b> <see cref="Advance"/> matches the edges out
/// of the snapshot's state, picks the FIRST whose guard passes, and takes that edge's
/// <see cref="TransitionDefinition{TState,TTrigger}.To"/> as the destination — so the destination and
/// the reducer come from the SAME chosen edge and cannot disagree. Guards for one (state, trigger) must
/// be mutually exclusive, which makes "first passing" match the TypeScript reducer exactly. There is no
/// external state-machine library: the snapshot's token IS the current state, so rehydrating to any
/// point is just reading it.</para>
///
/// <para><b>Every public operation is total.</b> <see cref="Advance"/> returns
/// <see cref="AdvanceResult"/> and <see cref="Rehydrate"/> returns <see cref="RehydrationResult"/>;
/// neither ever throws — an unpermitted trigger, a failed guard, or malformed stored JSON all
/// degrade to a typed value. That is the "no unhandled exception" guarantee the API and web layers
/// depend on.</para>
/// </summary>
public sealed class SnapshotMachine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly MachineDefinition<TState, TTrigger> _def;

    /// <summary>
    /// Creates an engine over <paramref name="definition"/>. The definition is not validated here; a
    /// machine built with <see cref="MachineBuilder{TState,TTrigger}"/> was checked by
    /// <see cref="MachineBuilder{TState,TTrigger}.Build"/>, and <see cref="BuiltMachine{TState,TTrigger}.Engine"/>
    /// already holds one.
    /// </summary>
    /// <param name="definition">The machine definition to interpret.</param>
    public SnapshotMachine(MachineDefinition<TState, TTrigger> definition) => _def = definition;

    /// <summary>The definition this engine interprets: states, transitions, validators and migrations.</summary>
    public MachineDefinition<TState, TTrigger> Definition => _def;

    /// <summary>
    /// Applies <paramref name="trigger"/> (with optional <paramref name="input"/>) to
    /// <paramref name="snapshot"/>. Returns the validated successor snapshot on success, or a typed
    /// rejection. Never throws.
    /// </summary>
    /// <remarks>
    /// Refusals, first match wins: an unknown state or trigger (<c>NO_TRANSITION</c>); an invoked train's
    /// outcome trigger, which only the outcome path applies (<c>OUTCOME_BOUND</c>); a state's timer trigger, which
    /// only the server fires through <see cref="AdvanceTimer"/> (<c>TIMER_BOUND</c>); no edge from the current
    /// state (<c>NO_TRANSITION</c>); an input that does not match the trigger's declared schema
    /// (<c>INVALID_INPUT</c>), checked before any guard reads it; a guard that declines (<c>GUARD_FAILED</c>);
    /// a target state whose timer reads a duration the successor context does not hold within its bounds
    /// (<c>INVALID_INPUT</c>); and a successor context the target state refuses (<c>INVALID_CONTEXT</c>).
    /// </remarks>
    public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        // Parse the wire tokens into the machine's enum space. Unknown tokens can fire nothing, so
        // they degrade to a handled rejection rather than a throw.
        if (!TryParseState(snapshot.State, out var fromState))
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"Unknown state '{snapshot.State}'."
            );
        // An outcome records what an invoked run produced. Taken from a caller, its input would be whatever the
        // caller sent and no run would have produced it.
        if (IsOutcomeTrigger(trigger))
            return new AdvanceResult.Rejected(
                RefusalCode.OutcomeBound,
                "This step is completed by the outcome of its work, not by an action."
            );
        // A timer is the server acting when a state has been held long enough; no caller fires it.
        if (IsTimerTrigger(trigger))
            return new AdvanceResult.Rejected(
                RefusalCode.TimerBound,
                "This step moves on by itself when its time is up, not by an action."
            );
        if (!TriggerNames.TryGetValue(trigger, out var triggerValue))
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"Unknown trigger '{trigger}'."
            );

        var context = snapshot.Context;

        var matches = _def
            .Transitions.Where(t => t.From.Equals(fromState) && t.Trigger.Equals(triggerValue))
            .ToList();
        if (matches.Count == 0)
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}'."
            );

        // The input is checked against the trigger's declared schema before any guard reads it, so a guard and a
        // reducer only ever see an input of the declared shape.
        if (InputProblem(triggerValue, input) is { } inputProblem)
            return new AdvanceResult.Rejected(RefusalCode.InvalidInput, inputProblem);

        try
        {
            // A guard is hand-written per runtime and may throw on unexpected input, so evaluate it inside
            // the totality backstop: a throwing guard degrades to internal-error rather than escaping
            // Advance (PD4). The chosen edge is the single source of BOTH the destination and the reducer —
            // no separate engine recomputes the target (which could disagree). Matches the TS engine.
            var chosen = matches.FirstOrDefault(t => GuardPasses(t, context, input));
            if (chosen is null)
            {
                var message =
                    matches.Select(m => m.GuardMessage).FirstOrDefault(m => m is not null)
                    ?? $"A transition from '{snapshot.State}' on '{trigger}' exists but its guard rejected the input.";
                return new AdvanceResult.Rejected(RefusalCode.GuardFailed, message);
            }

            var toState = chosen.To;

            var newContext =
                chosen.Reduce?.Invoke(context, input) ?? (JsonObject)context.DeepClone();
            return Enter(toState, newContext);
        }
        catch (Exception ex)
        {
            // Totality backstop: the engine must never surface an exception to a resolver. A guard, reducer or
            // validator is the author's code and its exception text can carry anything, so the detail is fixed
            // and the exception rides along for the server's log only.
            return new AdvanceResult.Rejected(
                RefusalCode.InternalError,
                "The transition failed with an unexpected error."
            )
            {
                Exception = ex,
            };
        }
    }

    /// <summary>
    /// Fires the timer <paramref name="trigger"/> (<c>&lt;State&gt;.after</c>) of the state <paramref name="snapshot"/>
    /// is in, the way the server applies a due timer: the context is carried to the timer's target unchanged. The
    /// separate entry point for timers; <see cref="Advance"/> refuses them. Never throws.
    /// </summary>
    /// <returns>
    /// The successor, or a rejection: <c>NO_TRANSITION</c> when <paramref name="trigger"/> is not the timer of the
    /// state the snapshot is in; <c>INVALID_INPUT</c> when the target's own timer reads a duration the context does not
    /// hold within its bounds; <c>INVALID_CONTEXT</c> when the target refuses the context.
    /// </returns>
    internal AdvanceResult AdvanceTimer(Snapshot snapshot, string trigger)
    {
        if (!TryParseState(snapshot.State, out var fromState))
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"Unknown state '{snapshot.State}'."
            );
        if (
            !TimerTriggers.TryParse(trigger, out var timedState)
            || timedState != snapshot.State
            || !_def.Timers.TryGetValue(fromState, out var timer)
        )
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"No timer fires from '{snapshot.State}' on '{trigger}'."
            );

        try
        {
            return Enter(timer.Target, (JsonObject)snapshot.Context.DeepClone());
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

    /// <summary>Whether <paramref name="trigger"/> names one of this machine's timer triggers (<c>Review.after</c>).</summary>
    internal bool IsTimerTrigger(string trigger) =>
        TimerTriggers.TryParse(trigger, out var state)
        && TryParseState(state, out var parsed)
        && _def.Timers.ContainsKey(parsed);

    /// <summary>The timer of the state named <paramref name="state"/>, or null when it has none.</summary>
    internal TimerDefinition<TState>? TimerOf(string state) =>
        TryParseState(state, out var parsed) && _def.Timers.TryGetValue(parsed, out var timer)
            ? timer
            : null;

    /// <summary>
    /// What a row written in the state named <paramref name="state"/> with <paramref name="context"/> does to its
    /// timer: a write that enters the state (<paramref name="restart"/>) arms it from now, any other keeps the due
    /// time the row was given at entry. A duration the context cannot give is the <c>Problem</c>.
    /// </summary>
    internal (SnapshotTimer? Timer, string? Problem) TimerFor(
        string state,
        JsonObject context,
        bool restart
    )
    {
        if (!TryParseState(state, out var parsed))
            return (SnapshotTimer.None, null);
        var (timer, problem) = _def.EnteringTimer(parsed, context);
        return timer is { Duration: not null } armed ? (armed with { Restart = restart }, null)
            : timer is { } none ? (none, null)
            : (null, problem);
    }

    // The successor of a transition into toState: the target's timer reads its duration from the context first, so a
    // duration the context does not hold is refused as input before the context is validated; then the target's
    // validator. The snapshot carries what its write does to the row's timer.
    private AdvanceResult Enter(TState toState, JsonObject newContext)
    {
        var (timer, timerProblem) = _def.EnteringTimer(toState, newContext);
        if (timerProblem is not null)
            return new AdvanceResult.Rejected(RefusalCode.InvalidInput, timerProblem);

        var contextError = _def.ValidateContext(toState, newContext);
        if (contextError is not null)
            return new AdvanceResult.Rejected(RefusalCode.InvalidContext, contextError);

        return new AdvanceResult.Transitioned(
            new Snapshot
            {
                Machine = _def.Id,
                Version = _def.Version,
                State = toState.ToString(),
                Context = newContext,
                Timer = timer,
            }
        );
    }

    /// <summary>
    /// Applies an invoked run's <paramref name="outcome"/> to <paramref name="snapshot"/>: the persistence layer
    /// calls this once it has matched the outcome to the entry that queued the run. A success tries the state's
    /// <c>OnDone</c> edges in declaration order and takes the first whose guard holds for the output; a failure or
    /// a cancel takes its one edge. The chosen edge's reduction produces the target's context, which must satisfy
    /// the target's validator. Never throws.
    /// </summary>
    /// <returns>
    /// The successor, or a rejection: <c>NO_TRANSITION</c> when the snapshot is not in a state that invokes a
    /// train, or no <c>OnDone</c> edge accepts the output; <c>INVALID_CONTEXT</c> when the reduction produced a
    /// context the target refuses; <c>INTERNAL_ERROR</c> when a custom handler threw.
    /// </returns>
    internal AdvanceResult ApplyOutcome(Snapshot snapshot, InvokeOutcome outcome) =>
        ApplyOutcomeCore(
            snapshot,
            outcome.Kind,
            outcome is InvokeOutcome.Done done ? done.Output : null,
            triggerState: null
        );

    /// <summary>
    /// Applies an outcome named by its trigger (<c>Fetching.done</c>) with <paramref name="input"/> as its input,
    /// as the TypeScript twin does on <c>applyOutcome</c>. The differential corpus replays outcome cases through this;
    /// <see cref="Advance"/> refuses every outcome trigger. Never throws.
    /// </summary>
    internal AdvanceResult AdvanceOutcome(Snapshot snapshot, string trigger, JsonNode? input) =>
        OutcomeTriggers.TryParse(trigger, out var state, out var kind)
            ? ApplyOutcomeCore(snapshot, kind, input, state)
            : new AdvanceResult.Rejected(RefusalCode.NoTransition, $"Unknown trigger '{trigger}'.");

    /// <summary>Whether <paramref name="trigger"/> names one of this machine's outcome triggers.</summary>
    internal bool IsOutcomeTrigger(string trigger) =>
        OutcomeTriggers.TryParse(trigger, out var state, out _)
        && TryParseState(state, out var parsed)
        && _def.Invokes.ContainsKey(parsed);

    private AdvanceResult ApplyOutcomeCore(
        Snapshot snapshot,
        InvokeOutcomeKind kind,
        JsonNode? input,
        string? triggerState
    )
    {
        if (!TryParseState(snapshot.State, out var fromState))
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"Unknown state '{snapshot.State}'."
            );
        var trigger = OutcomeTriggers.Name(fromState, kind);
        if (triggerState is not null && triggerState != snapshot.State)
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{triggerState}.{OutcomeTriggers.Suffix(kind)}'."
            );
        if (!_def.Invokes.TryGetValue(fromState, out var invoke))
            return new AdvanceResult.Rejected(
                RefusalCode.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}': the state invokes no train."
            );

        // A success outcome's input is the train's output, checked against the output type's schema like any
        // trigger input; a failure or a cancel carries none.
        var outputSchema =
            kind == InvokeOutcomeKind.Done ? invoke.OutputSchema : ContextSchema.Empty;
        if (SchemaValidator.ValidateInput(outputSchema, input) is { } outputProblem)
            return new AdvanceResult.Rejected(RefusalCode.InvalidInput, outputProblem);

        try
        {
            var context = snapshot.Context;
            var chosen = invoke
                .EdgesFor(kind)
                .FirstOrDefault(e => e.CompiledGuard is null || e.CompiledGuard(context, input));
            // An output no OnDone edge accepts has nowhere to go. It is not a guard the caller can satisfy by
            // sending something else, so it is a no-transition rather than a guard-failed.
            if (chosen is null)
                return new AdvanceResult.Rejected(
                    RefusalCode.NoTransition,
                    $"No OnDone edge from '{snapshot.State}' accepts the train's output."
                );

            var newContext =
                chosen.CompiledReduce?.Invoke(context, input) ?? (JsonObject)context.DeepClone();
            return Enter(chosen.To, newContext);
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

    /// <summary>
    /// Parses and validates stored JSON (e.g. a jsonb column) into a <see cref="Snapshot"/>.
    /// This is the "parse, don't validate" boundary: bad data becomes a typed
    /// <see cref="RehydrationResult.Error"/>, never a throw.
    /// </summary>
    public RehydrationResult Rehydrate(string json)
    {
        // Totality backstop: the engine must NEVER surface an exception to a resolver (the same
        // guarantee Advance already has). Any parse/validation path that throws — a non-integral
        // version, a hostile payload — degrades to a typed Error here, never an unhandled 500.
        try
        {
            return RehydrateCore(json);
        }
        catch (Exception ex)
        {
            // A migration is the author's code too: the message is fixed and the exception kept for the log.
            return new RehydrationResult.Error(
                RefusalCode.Malformed,
                "The snapshot could not be read."
            )
            {
                Exception = ex,
            };
        }
    }

    private RehydrationResult RehydrateCore(string json)
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
        var stateToken = AsString(obj["state"]);
        var version = AsInt(obj["version"]);

        if (
            machine is null
            || stateToken is null
            || version is null
            || obj["context"] is not JsonObject context
        )
            return new RehydrationResult.Error(
                RefusalCode.Malformed,
                "Snapshot is missing required fields (machine, version, state, context)."
            );

        if (machine != _def.Id)
            return new RehydrationResult.Error(
                RefusalCode.UnknownMachine,
                $"Snapshot machine '{machine}' does not match '{_def.Id}'."
            );

        // Bring the snapshot up to the current version by applying forward migrations in sequence. A
        // snapshot newer than the definition, or a gap in the migration chain, is a version-mismatch.
        var effectiveState = stateToken;
        var effectiveContext = (JsonObject)context.DeepClone();
        var effectiveVersion = version.Value;

        if (effectiveVersion > _def.Version)
            return new RehydrationResult.Error(
                RefusalCode.VersionMismatch,
                $"Snapshot version {effectiveVersion} is newer than definition version {_def.Version}."
            );

        while (effectiveVersion < _def.Version)
        {
            if (!_def.Migrations.TryGetValue(effectiveVersion, out var migrate))
                return new RehydrationResult.Error(
                    RefusalCode.VersionMismatch,
                    $"No migration from version {effectiveVersion} to {_def.Version}."
                );

            var migrated = migrate(effectiveState, effectiveContext);
            effectiveState = migrated.State;
            effectiveContext = migrated.Context;
            effectiveVersion++;
        }

        if (!TryParseState(effectiveState, out var state))
            return new RehydrationResult.Error(
                RefusalCode.UnknownState,
                $"Unknown state '{effectiveState}'."
            );

        // A value with no canonical wire (a number outside double range) or that no store can hold (a NUL
        // character) is refused here, before anything persists it: accepting it would store a row that every
        // later read fails to serialize.
        if (StorableJson.Problem(effectiveContext) is { } unstorable)
            return new RehydrationResult.Error(RefusalCode.Malformed, unstorable);

        var contextError = _def.ValidateContext(state, effectiveContext);
        if (contextError is not null)
            return new RehydrationResult.Error(RefusalCode.InvalidContext, contextError);

        return new RehydrationResult.Ok(
            new Snapshot
            {
                Machine = machine,
                Version = _def.Version,
                // The declared name, never the token as sent, so what is stored and served is canonical.
                State = state.ToString(),
                Context = effectiveContext,
            }
        );
    }

    /// <summary>Serializes a snapshot to the canonical JSON shape (round-trips through <see cref="Rehydrate"/>).</summary>
    public string Serialize(Snapshot snapshot)
    {
        // The envelope order (machine, version, state, context) is fixed by construction on both sides; the
        // CONTEXT is canonicalized per RFC 8785 (JCS) — keys sorted, ECMAScript number formatting, and
        // JSON.stringify string escaping — so the bytes are identical to the TypeScript twin's regardless of
        // how the object was built. That byte-equality is the prerequisite for any hash/signature over a
        // stored snapshot and for the differential's byte-exact compare.
        return CanonicalJson.SerializeSnapshot(
            snapshot.Machine,
            snapshot.Version,
            snapshot.State,
            snapshot.Context
        );
    }

    /// <summary>
    /// Whether firing <paramref name="trigger"/> now would succeed (a transition exists from the
    /// snapshot's state and its guard passes for the given input). Use it to decide whether to expose
    /// an action. Never throws.
    /// </summary>
    public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        if (!TryParseState(snapshot.State, out var fromState))
            return false;
        if (!TriggerNames.TryGetValue(trigger, out var triggerValue))
            return false;

        if (InputProblem(triggerValue, input) is not null)
            return false;

        try
        {
            return _def.Transitions.Any(t =>
                t.From.Equals(fromState)
                && t.Trigger.Equals(triggerValue)
                && GuardPasses(t, snapshot.Context, input)
            );
        }
        catch (Exception)
        {
            // A guard is the author's code; one that throws cannot fire, and CanFire never throws.
            return false;
        }
    }

    // Why the input does not match the trigger's declared schema, or null when it does or the trigger declares
    // none to check (a raw-delegate machine, whose guards read the input themselves).
    private string? InputProblem(TTrigger trigger, JsonNode? input) =>
        _def.InputSchemas.TryGetValue(trigger, out var schema)
            ? SchemaValidator.ValidateInput(schema, input)
            : null;

    /// <summary>
    /// The distinct triggers that have any transition out of the snapshot's state — the actions worth
    /// exposing. Combine with <see cref="CanFire"/> for enablement. Never throws.
    /// </summary>
    public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot)
    {
        if (!TryParseState(snapshot.State, out var fromState))
            return Array.Empty<string>();

        return _def
            .Transitions.Where(t => t.From.Equals(fromState))
            .Select(t => t.Trigger.ToString())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Reduces this definition to its canonical, language-neutral <b>structure</b> — id, version,
    /// initial state, the full state and trigger sets, and the transition edges — with everything
    /// sorted deterministically. Guards/reducers (behavior) are deliberately excluded; only shape.
    /// The C# and TypeScript engines emit an identical structure for the same machine, and both assert
    /// it against a committed golden file, so a structural divergence between the two definitions
    /// (a new/renamed/removed state, trigger, or edge on one side only) fails the build.
    /// </summary>
    public JsonObject Describe()
    {
        var states = Enum.GetValues<TState>()
            .Select(s => s.ToString()!)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // An invoking state's outcome edges are edges too, under their outcome trigger (Fetching.done), as the
        // TypeScript twin lists them.
        var edges = _def
            .Transitions.Select(t =>
                (From: t.From.ToString()!, Trigger: t.Trigger.ToString()!, To: t.To.ToString()!)
            )
            .Concat(
                _def.Invokes.Values.SelectMany(invoke =>
                    Enum.GetValues<InvokeOutcomeKind>()
                        .SelectMany(kind =>
                            invoke
                                .EdgesFor(kind)
                                .Select(e =>
                                    (
                                        From: invoke.State.ToString()!,
                                        Trigger: invoke.TriggerName(kind),
                                        To: e.To.ToString()!
                                    )
                                )
                        )
                )
            )
            .ToList();

        var triggers = edges
            .Select(t => t.Trigger)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var transitions = edges
            .OrderBy(t => t.From, StringComparer.Ordinal)
            .ThenBy(t => t.Trigger, StringComparer.Ordinal)
            .ThenBy(t => t.To, StringComparer.Ordinal)
            .ToArray();

        var statesArray = new JsonArray();
        foreach (var s in states)
            statesArray.Add((JsonNode)s);

        var triggersArray = new JsonArray();
        foreach (var t in triggers)
            triggersArray.Add((JsonNode)t);

        var transitionsArray = new JsonArray();
        foreach (var t in transitions)
            transitionsArray.Add(
                new JsonObject
                {
                    ["from"] = t.From,
                    ["trigger"] = t.Trigger,
                    ["to"] = t.To,
                }
            );

        return new JsonObject
        {
            ["id"] = _def.Id,
            ["version"] = _def.Version,
            ["initialState"] = _def.InitialState.ToString(),
            ["states"] = statesArray,
            ["triggers"] = triggersArray,
            ["transitions"] = transitionsArray,
        };
    }

    private static bool GuardPasses(
        TransitionDefinition<TState, TTrigger> t,
        JsonObject context,
        JsonNode? input
    ) => t.Guard is null || t.Guard(context, input);

    // Exact-name lookups for the wire tokens. Enum.TryParse also accepts the numeric value ("1"), a padded
    // name (" Unlocked") and a flags list ("Locked, Unlocked"), none of which the TypeScript twin accepts, and
    // an accepted alias would then be stored and served as the state. Only a declared name, compared
    // ordinally, is a state or a trigger.
    private static readonly Dictionary<string, TState> StateNames = NamesOf<TState>();
    private static readonly Dictionary<string, TTrigger> TriggerNames = NamesOf<TTrigger>();

    private static Dictionary<string, T> NamesOf<T>()
        where T : struct, Enum =>
        Enum.GetNames<T>().ToDictionary(name => name, Enum.Parse<T>, StringComparer.Ordinal);

    private static bool TryParseState(string token, out TState state) =>
        StateNames.TryGetValue(token, out state);

    private static string? AsString(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    // Accept any INTEGRAL JSON number, matching TypeScript (`Number.isInteger`), which cannot distinguish
    // `1` from `1.0` — JSON has one number type there. Widening C# to agree is the only symmetric choice:
    // TS literally cannot represent the distinction, so C# must not reject on it. Non-integral or
    // out-of-range numbers return null (a typed "missing/invalid" upstream), never a throw.
    private static int? AsInt(JsonNode? node)
    {
        if (node?.GetValueKind() != JsonValueKind.Number)
            return null;
        if (!node.AsValue().TryGetValue<double>(out var d))
            return null;
        if (!double.IsInteger(d) || d < int.MinValue || d > int.MaxValue)
            return null;
        return (int)d;
    }
}
