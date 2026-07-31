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

    public SnapshotMachine(MachineDefinition<TState, TTrigger> definition) => _def = definition;

    public MachineDefinition<TState, TTrigger> Definition => _def;

    /// <summary>
    /// Applies <paramref name="trigger"/> (with optional <paramref name="input"/>) to
    /// <paramref name="snapshot"/>. Returns the validated successor snapshot on success, or a typed
    /// rejection. Never throws.
    /// </summary>
    public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input = null)
    {
        // Parse the wire tokens into the machine's enum space. Unknown tokens can fire nothing, so
        // they degrade to a handled rejection rather than a throw.
        if (!TryParseState(snapshot.State, out var fromState))
            return new AdvanceResult.Rejected(
                RejectionReasons.NoTransition,
                $"Unknown state '{snapshot.State}'."
            );
        if (
            !Enum.TryParse<TTrigger>(trigger, ignoreCase: false, out var triggerValue)
            || !Enum.IsDefined(triggerValue)
        )
            return new AdvanceResult.Rejected(
                RejectionReasons.NoTransition,
                $"Unknown trigger '{trigger}'."
            );

        var context = snapshot.Context;

        var matches = _def
            .Transitions.Where(t => t.From.Equals(fromState) && t.Trigger.Equals(triggerValue))
            .ToList();
        if (matches.Count == 0)
            return new AdvanceResult.Rejected(
                RejectionReasons.NoTransition,
                $"No transition from '{snapshot.State}' on '{trigger}'."
            );

        var chosen = matches.FirstOrDefault(t => GuardPasses(t, context, input));
        if (chosen is null)
        {
            var message =
                matches.Select(m => m.GuardMessage).FirstOrDefault(m => m is not null)
                ?? $"A transition from '{snapshot.State}' on '{trigger}' exists but its guard rejected the input.";
            return new AdvanceResult.Rejected(RejectionReasons.GuardFailed, message);
        }

        try
        {
            // The chosen edge is the single source of BOTH the destination and the reducer — no separate
            // engine recomputes the target (which could disagree). Matches the TS reducer's `chosen.to`.
            var toState = chosen.To;

            var newContext =
                chosen.Reduce?.Invoke(context, input) ?? (JsonObject)context.DeepClone();
            var contextError = _def.ValidateContext(toState, newContext);
            if (contextError is not null)
                return new AdvanceResult.Rejected(RejectionReasons.InvalidContext, contextError);

            return new AdvanceResult.Transitioned(
                new Snapshot
                {
                    Machine = _def.Id,
                    Version = _def.Version,
                    State = toState.ToString(),
                    Context = newContext,
                }
            );
        }
        catch (Exception ex)
        {
            // Totality backstop: the engine must never surface an exception to a resolver.
            return new AdvanceResult.Rejected(RejectionReasons.InternalError, ex.Message);
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
            return new RehydrationResult.Error(RehydrationErrorCodes.Malformed, ex.Message);
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
            return new RehydrationResult.Error(
                RehydrationErrorCodes.Malformed,
                "Input is not valid JSON."
            );
        }

        if (node is not JsonObject obj)
            return new RehydrationResult.Error(
                RehydrationErrorCodes.Malformed,
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
                RehydrationErrorCodes.Malformed,
                "Snapshot is missing required fields (machine, version, state, context)."
            );

        if (machine != _def.Id)
            return new RehydrationResult.Error(
                RehydrationErrorCodes.UnknownMachine,
                $"Snapshot machine '{machine}' does not match '{_def.Id}'."
            );

        // Bring the snapshot up to the current version by applying forward migrations in sequence. A
        // snapshot newer than the definition, or a gap in the migration chain, is a version-mismatch.
        var effectiveState = stateToken;
        var effectiveContext = (JsonObject)context.DeepClone();
        var effectiveVersion = version.Value;

        if (effectiveVersion > _def.Version)
            return new RehydrationResult.Error(
                RehydrationErrorCodes.VersionMismatch,
                $"Snapshot version {effectiveVersion} is newer than definition version {_def.Version}."
            );

        while (effectiveVersion < _def.Version)
        {
            if (!_def.Migrations.TryGetValue(effectiveVersion, out var migrate))
                return new RehydrationResult.Error(
                    RehydrationErrorCodes.VersionMismatch,
                    $"No migration from version {effectiveVersion} to {_def.Version}."
                );

            var migrated = migrate(effectiveState, effectiveContext);
            effectiveState = migrated.State;
            effectiveContext = migrated.Context;
            effectiveVersion++;
        }

        if (!TryParseState(effectiveState, out var state))
            return new RehydrationResult.Error(
                RehydrationErrorCodes.UnknownState,
                $"Unknown state '{effectiveState}'."
            );

        var contextError = _def.ValidateContext(state, effectiveContext);
        if (contextError is not null)
            return new RehydrationResult.Error(RehydrationErrorCodes.InvalidContext, contextError);

        return new RehydrationResult.Ok(
            new Snapshot
            {
                Machine = machine,
                Version = _def.Version,
                State = effectiveState,
                Context = effectiveContext,
            }
        );
    }

    /// <summary>Serializes a snapshot to the canonical JSON shape (round-trips through <see cref="Rehydrate"/>).</summary>
    public string Serialize(Snapshot snapshot)
    {
        var obj = new JsonObject
        {
            ["machine"] = snapshot.Machine,
            ["version"] = snapshot.Version,
            ["state"] = snapshot.State,
            // The envelope order (machine, version, state, context) is fixed by construction on both
            // sides; the CONTEXT's key order is data-dependent, so it is canonicalized (RFC 8785 key
            // sorting) to make the serialized bytes identical regardless of how the object was built —
            // the prerequisite for any hash/signature/byte-equality over a stored snapshot.
            ["context"] = Canonicalize(snapshot.Context),
        };
        return obj.ToJsonString();
    }

    // RFC 8785 §3.2.3: sort object members by UTF-16 code unit (ordinal), recursively. Arrays keep order.
    private static JsonNode? Canonicalize(JsonNode? node) =>
        node switch
        {
            JsonObject obj => SortObject(obj),
            JsonArray arr => new JsonArray(arr.Select(Canonicalize).ToArray()),
            _ => node?.DeepClone(),
        };

    private static JsonObject SortObject(JsonObject obj)
    {
        var sorted = new JsonObject();
        foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sorted[kv.Key] = Canonicalize(kv.Value);
        return sorted;
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
        if (
            !Enum.TryParse<TTrigger>(trigger, ignoreCase: false, out var triggerValue)
            || !Enum.IsDefined(triggerValue)
        )
            return false;

        return _def.Transitions.Any(t =>
            t.From.Equals(fromState)
            && t.Trigger.Equals(triggerValue)
            && GuardPasses(t, snapshot.Context, input)
        );
    }

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

        var triggers = _def
            .Transitions.Select(t => t.Trigger.ToString()!)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var transitions = _def
            .Transitions.Select(t =>
                (From: t.From.ToString()!, Trigger: t.Trigger.ToString()!, To: t.To.ToString()!)
            )
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

    private static bool TryParseState(string token, out TState state) =>
        Enum.TryParse(token, ignoreCase: false, out state) && Enum.IsDefined(state);

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
