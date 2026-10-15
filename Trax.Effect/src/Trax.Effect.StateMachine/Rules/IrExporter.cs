using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Exports a declaratively-authored machine to the neutral IR (the formalized machine.json): identity,
/// structure, per-state context schema, and per-transition guard/reducer as DATA, plus committed states and
/// effect bindings. This is the C# side of the source-and-oracle inversion, everything a generator needs to
/// emit a per-language runtime lives here, exported from the one C# source. Output is canonical JSON so it is
/// a stable golden.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/ir-format">SDK reference</seealso>
public static class IrExporter
{
    /// <summary>
    /// The IR format this exporter writes: the contract the TypeScript engine reads it by. A twin generated with
    /// specifier imports records it, and the installed <c>@traxsharp/state-machine</c> refuses a format it cannot
    /// read. It is not written into the IR, so it moves no committed <c>ir.json</c> and no schema hash. Bump it,
    /// with <c>irFormatVersion</c> in <c>Trax.Api.StateMachine/src/rules/irFormat.ts</c>, when the IR gains
    /// something an engine reading the previous format would misread or ignore; <c>IrFormatParityTests</c> holds
    /// the two equal.
    /// </summary>
    internal const int FormatVersion = 1;

    /// <summary>
    /// Exports <paramref name="machine"/> as single-line canonical IR JSON (stable key order, so the output
    /// can be committed and hashed). The <c>invariants</c> and <c>differential</c> sections are emitted only
    /// when the machine declares them.
    /// </summary>
    /// <param name="machine">The result of <see cref="MachineBuilder{TState,TTrigger}.Build"/>.</param>
    /// <returns>The IR as canonical JSON.</returns>
    /// <exception cref="InvalidOperationException">
    /// The machine was authored with raw delegates (<see cref="BuiltMachine{TState,TTrigger}.Declarative"/>
    /// is null), so there is no declarative model to export; or it mixes the styles and still has a delegate
    /// guard (<c>When(Func...)</c>), delegate reducer (<c>Reduce(Func...)</c>) or delegate validator
    /// (<c>Holds</c>), which the IR cannot carry.
    /// </exception>
    public static string Export<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var declarative =
            machine.Declarative
            ?? throw new InvalidOperationException(
                "IR export requires a declaratively-authored machine (use .Context/.When/.Reduce, not raw delegates)."
            );
        var def = machine.Definition;
        RefuseDelegates(machine, declarative);

        var context = new JsonObject();
        foreach (var (state, schema) in declarative.ContextSchemas)
            context[state.ToString()!] = WriteSchema(schema);

        // Only what a client may send: an invoked train's output is exported under its outcome instead.
        var inputs = new JsonObject();
        foreach (var (trigger, schema) in declarative.TriggerInputs)
            inputs[trigger.ToString()!] = WriteSchema(schema);

        var transitions = BuildTransitions(machine, declarative);

        var ir = new JsonObject
        {
            ["id"] = def.Id,
            ["version"] = def.Version,
            ["initialState"] = def.InitialState.ToString(),
            // The machine's actual initial context (from StartsAt), so a generated runtime reproduces it
            // exactly. Deriving it from the schema is not enough: a field with a constrained default (an enum
            // like legislature="federal") would default to "" and fail its own OneOf constraint.
            ["initialContext"] = def.CreateInitialContext(),
            ["states"] = ToSortedArray(Enum.GetNames<TState>()),
            ["triggers"] = ToSortedArray(Enum.GetNames<TTrigger>()),
            ["committedStates"] = ToSortedArray(
                machine.CommittedStates.Select(s => s.ToString()!).ToArray()
            ),
            ["context"] = context,
            ["inputs"] = inputs,
            ["transitions"] = transitions,
        };

        // Per-state invariants (the .Requires(...) policy, on top of the context schema). Omitted when a
        // machine has none, so a shape-only machine's IR is unchanged.
        if (declarative.StateInvariants.Count > 0)
        {
            var invariants = new JsonObject();
            foreach (var (state, rule) in declarative.StateInvariants)
                invariants[state.ToString()!] = WriteRule(rule);
            ir["invariants"] = invariants;
        }

        // The outcome triggers of states that invoke a train, a trigger kind of their own. Omitted when the machine
        // invokes nothing, so its IR is unchanged.
        if (def.Invokes.Count > 0)
            ir["outcomes"] = WriteOutcomes(def.Invokes);

        // The timers of timed states, a server-only trigger kind of their own: never in inputs or triggers, which name
        // what a client sends. Omitted when the machine has none, so its IR is unchanged.
        if (def.Timers.Count > 0)
            ir["timers"] = WriteTimers(machine);

        // The differential fuzzing inputs (test-only), if authored via .Differential(...). Carries the
        // samples/seeds/contexts the cross-language harness enumerates, so it reads this one IR instead of a
        // hand-written machine.json. Omitted when empty, so a machine without .Differential has identical IR.
        if (declarative.Differential is { IsEmpty: false } differential)
            ir["differential"] = WriteDifferential(differential);

        return CanonicalJson.Serialize(ir);
    }

    // A delegate is opaque, so the IR would carry its edge with no guard or reducer (which a generated twin reads
    // as "always taken, keep the context") and its state with no validator, while the server runs the delegate.
    // The twin would accept what the server refuses. Refuse the export instead, naming every offender.
    private static void RefuseDelegates<TState, TTrigger>(
        BuiltMachine<TState, TTrigger> machine,
        DeclarativeModel<TState, TTrigger> declarative
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var problems = DelegateProblems(machine, declarative);
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"IR export refuses the machine '{machine.Definition.Id}': the IR cannot carry a C# delegate, so a "
                    + "generated twin would accept what the server refuses. "
                    + string.Join(" ", problems.Select(p => char.ToUpperInvariant(p[0]) + p[1..]))
            );
    }

    /// <summary>
    /// Whether <see cref="Export"/> refuses <paramref name="machine"/> by design: it was authored with raw
    /// delegates (no declarative model), or it mixes the styles and still has a delegate guard, reducer or
    /// validator. Such a machine has no IR, so it has no IR hash; any other export failure is a defect.
    /// </summary>
    internal static bool RefusesByDesign<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum =>
        machine.Declarative is not { } declarative
        || DelegateProblems(machine, declarative).Count > 0;

    private static List<string> DelegateProblems<TState, TTrigger>(
        BuiltMachine<TState, TTrigger> machine,
        DeclarativeModel<TState, TTrigger> declarative
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var def = machine.Definition;
        var problems = new List<string>();

        // The builder records the engine transition and its declarative twin in lockstep, and a declarative
        // When/Reduce sets both, so an engine delegate with no matching rule or reduction is a raw delegate.
        for (var i = 0; i < def.Transitions.Count; i++)
        {
            var td = def.Transitions[i];
            var dt = i < declarative.Transitions.Count ? declarative.Transitions[i] : null;
            var edge = $"{td.From} -{td.Trigger}-> {td.To}";
            if (td.Guard is not null && dt?.Guard is null)
                problems.Add(
                    $"the edge {edge} has a delegate guard (When(Func...)). Use When(Rule), or for logic the rule "
                        + "vocabulary cannot express, `.When(new Rule.Custom(\"name\"))` with "
                        + "`.CustomGuard(\"name\", (context, input) => ...)`."
                );
            if (td.Reduce is not null && dt?.Reduce is null)
                problems.Add(
                    $"the edge {edge} has a delegate reducer (Reduce(Func...)). Use Reduce(Reduction), or for logic "
                        + "the vocabulary cannot express, `.Reduce(new Reduction.Custom(\"name\"))` with "
                        + "`.CustomReducer(\"name\", (context, input) => ...)`."
                );
        }

        var delegateStates = def
            .ContextValidators.Keys.Where(state =>
                declarative.DelegateValidatedStates.Contains(state)
                || (
                    !declarative.ContextSchemas.ContainsKey(state)
                    && !declarative.StateInvariants.ContainsKey(state)
                )
            )
            .OrderBy(state => state.ToString(), StringComparer.Ordinal);
        foreach (var state in delegateStates)
            problems.Add(
                $"the state {state} has a delegate validator (Holds). Use Context<T>() and Requires(Rule), or for "
                    + "a check the vocabulary cannot express, `.Requires(new Rule.Custom(\"name\"))` with "
                    + "`.CustomGuard(\"name\", (context, input) => ...)`."
            );

        foreach (
            var timer in def
                .Timers.Values.Where(t => t.Fixed is null && t.Field is null)
                .OrderBy(t => t.State.ToString(), StringComparer.Ordinal)
        )
            problems.Add(
                $"the timer of {timer.State} reads its duration through a selector that is not a single field. Read "
                    + "it from one field of the state's context: `.After<TContext>(c => c.Field, min, max)`."
            );

        return problems;
    }

    // One entry per timed state, sorted by state: the state, its trigger, its target, the duration (a fixed `after`, or
    // the context `field` with its bounds, as ISO-8601 durations), and the rules it acts under, the union of the rules
    // of everything that enters the state, for the census and the startup comparison.
    private static JsonArray WriteTimers<TState, TTrigger>(BuiltMachine<TState, TTrigger> machine)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var rules = machine.TimerRules();
        var timers = new JsonArray();
        foreach (
            var timer in machine.Timers.Values.OrderBy(
                t => t.State.ToString(),
                StringComparer.Ordinal
            )
        )
        {
            var acting = new JsonArray();
            foreach (var rule in rules.GetValueOrDefault(timer.TriggerName) ?? [])
                acting.Add(rule);
            var written = new JsonObject
            {
                ["state"] = timer.State.ToString(),
                ["trigger"] = timer.TriggerName,
                ["target"] = timer.Target.ToString(),
                ["rules"] = acting,
            };
            if (timer.Fixed is { } fixedDuration)
                written["after"] = IsoDuration.Format(fixedDuration);
            else
                written["field"] = new JsonObject
                {
                    ["name"] = timer.Field,
                    ["min"] = IsoDuration.Format(timer.Min),
                    ["max"] = IsoDuration.Format(timer.Max),
                };
            timers.Add(written);
        }
        return timers;
    }

    private static JsonObject WriteDifferential<TState, TTrigger>(
        DifferentialModel<TState, TTrigger> diff
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var obj = new JsonObject();

        if (diff.Samples.Count > 0 || diff.OutcomeSamples.Count > 0)
        {
            var samples = new JsonObject();
            foreach (var (trigger, inputs) in diff.Samples)
                samples[trigger.ToString()!] = Copy(inputs);
            foreach (var (state, outputs) in diff.OutcomeSamples)
                samples[OutcomeTriggers.Name(state, InvokeOutcomeKind.Done)] = Copy(outputs);
            obj["samples"] = samples;
        }

        if (diff.Seeds.Count > 0)
        {
            var seeds = new JsonObject();
            // One seed is written as the context itself and several as a list of them, so the IR of a machine that
            // seeds each state once is the same as before a state could take more than one, and so is its hash.
            foreach (var (state, contexts) in diff.Seeds)
                seeds[state.ToString()!] =
                    contexts.Count == 1 ? contexts[0].DeepClone() : Copy(contexts);
            obj["seeds"] = seeds;
        }

        if (diff.Contexts.Count > 0)
        {
            var contexts = new JsonArray();
            foreach (var context in diff.Contexts)
                contexts.Add(context.DeepClone());
            obj["contexts"] = contexts;
        }

        return obj;
    }

    private static JsonArray Copy(IEnumerable<JsonNode> nodes)
    {
        var arr = new JsonArray();
        foreach (var node in nodes)
            arr.Add(node.DeepClone());
        return arr;
    }

    // One entry per outcome trigger (Fetching.done, Fetching.failed, Fetching.cancelled): the invoking state, the
    // outcome, the train's canonical name, and the edges in declaration order, which is the order they are tried
    // in, so a guarded edge before an unguarded fallback stays before it. The success outcome also carries the
    // schema of the train's output, which is its input. The input mapping is server-only and is not exported: the
    // twin never starts a train.
    private static JsonObject WriteOutcomes<TState>(
        IReadOnlyDictionary<TState, InvokeDefinition<TState>> invokes
    )
        where TState : struct, Enum
    {
        var outcomes = new JsonObject();
        foreach (var invoke in invokes.Values)
        foreach (var kind in Enum.GetValues<InvokeOutcomeKind>())
        {
            var edges = new JsonArray();
            foreach (var edge in invoke.EdgesFor(kind))
            {
                var e = new JsonObject { ["to"] = edge.To.ToString() };
                if (edge.Guard is not null)
                    e["guard"] = WriteRule(edge.Guard);
                if (edge.Reduce is not null)
                    e["reduce"] = WriteReduction(edge.Reduce);
                edges.Add(e);
            }
            var outcome = new JsonObject
            {
                ["state"] = invoke.State.ToString(),
                ["outcome"] = OutcomeTriggers.Suffix(kind),
                ["edges"] = edges,
            };
            if (kind == InvokeOutcomeKind.Done)
                outcome["output"] = WriteSchema(invoke.OutputSchema);
            outcomes[invoke.TriggerName(kind)] = outcome;
        }
        return outcomes;
    }

    private static JsonArray BuildTransitions<TState, TTrigger>(
        BuiltMachine<TState, TTrigger> machine,
        DeclarativeModel<TState, TTrigger> declarative
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        // DeclarativeModel.Transitions and Definition.Transitions are built in lockstep (same index/order).
        var rows = new List<(string From, string Trigger, string To, JsonObject Json)>();
        for (var i = 0; i < declarative.Transitions.Count; i++)
        {
            var dt = declarative.Transitions[i];
            var td = machine.Definition.Transitions[i];
            var from = dt.From.ToString()!;
            var trigger = dt.Trigger.ToString()!;
            var to = dt.To.ToString()!;

            var obj = new JsonObject
            {
                ["from"] = from,
                ["trigger"] = trigger,
                ["to"] = to,
            };
            if (dt.Guard is not null)
                obj["guard"] = WriteRule(dt.Guard);
            if (td.GuardMessage is not null)
                obj["guardMessage"] = td.GuardMessage;
            if (dt.Reduce is not null)
                obj["reduce"] = WriteReduction(dt.Reduce);

            var effect = machine.Effects.FirstOrDefault(e =>
                e.From.ToString() == from
                && e.Trigger.ToString() == trigger
                && e.To.ToString() == to
            );
            // The IR reaches every browser: the edge says it is effect-bound and which claim keys it writes, and
            // the effect's type stays on the server.
            if (effect is not null)
                obj["effect"] = new JsonObject { ["keyPrefix"] = effect.KeyPrefix };

            rows.Add((from, trigger, to, obj));
        }

        var array = new JsonArray();
        foreach (
            var row in rows.OrderBy(r => r.From, StringComparer.Ordinal)
                .ThenBy(r => r.Trigger, StringComparer.Ordinal)
                .ThenBy(r => r.To, StringComparer.Ordinal)
        )
            array.Add(row.Json);
        return array;
    }

    private static JsonObject WriteSchema(ContextSchema schema) =>
        new() { ["fields"] = WriteFields(schema.Fields) };

    // A nested record's fields, or a list of records' element fields, are written under the field as "fields".
    private static JsonArray WriteFields(IReadOnlyList<FieldSchema> schema)
    {
        var fields = new JsonArray();
        foreach (var field in schema)
        {
            var constraints = new JsonArray();
            foreach (var constraint in field.Constraints)
                constraints.Add(WriteRule(constraint));

            var written = new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = TypeName(field.Type),
                ["nullable"] = field.Nullable,
                ["constraints"] = constraints,
            };
            WriteShape(written, field);
            fields.Add(written);
        }
        return fields;
    }

    // What a field and a dictionary value or list element share: a number's range, an enum's names (and which match
    // only as written), a string's format, a record's fields, and the schema of its own values and keys. Each is
    // written only when the field has it.
    private static void WriteShape(JsonObject written, FieldSchema field)
    {
        if (field.Minimum is { } minimum)
            written["minimum"] = minimum;
        if (field.Maximum is { } maximum)
            written["maximum"] = maximum;
        if (field.Enum is { } names)
            written["enum"] = new JsonArray(names.Select(n => (JsonNode?)n).ToArray());
        if (field.EnumExact is { } exact)
            written["enumExact"] = new JsonArray(exact.Select(n => (JsonNode?)n).ToArray());
        if (field.Format is { } format)
            written["format"] = format;
        if (field.Fields is { } nested)
            written["fields"] = WriteFields(nested);
        if (field.Values is { } values)
        {
            var value = new JsonObject
            {
                ["type"] = TypeName(values.Type),
                ["nullable"] = values.Nullable,
            };
            WriteShape(value, values);
            written["values"] = value;
        }
        if (field.Keys is { } keys)
        {
            var key = new JsonObject { ["type"] = TypeName(keys.Type), ["nullable"] = false };
            WriteShape(key, keys);
            written["keys"] = key;
        }
    }

    private static JsonNode WriteRule(Rule rule)
    {
        switch (rule)
        {
            case Rule.Present r:
                return FieldRule("present", r.Source, r.Field);
            case Rule.Absent r:
                return FieldRule("absent", r.Source, r.Field);
            case Rule.NonEmpty r:
                return FieldRule("nonEmpty", r.Source, r.Field);
            case Rule.OfType r:
            {
                var o = FieldRule("ofType", r.Source, r.Field);
                o["type"] = TypeName(r.Type);
                return o;
            }
            case Rule.OneOf r:
            {
                var o = FieldRule("oneOf", r.Source, r.Field);
                var values = new JsonArray();
                foreach (var v in r.Values)
                    values.Add(v);
                o["values"] = values;
                return o;
            }
            case Rule.Compare r:
            {
                var o = FieldRule("compare", r.Source, r.Field);
                o["op"] = OpName(r.Op);
                o["value"] = r.Value;
                return o;
            }
            case Rule.Count r:
            {
                var o = FieldRule("count", r.Source, r.Field);
                o["op"] = OpName(r.Op);
                o["value"] = r.Value;
                return o;
            }
            case Rule.Length r:
            {
                var o = FieldRule("length", r.Source, r.Field);
                o["op"] = OpName(r.Op);
                o["value"] = r.Value;
                return o;
            }
            case Rule.BoolEquals r:
            {
                var o = FieldRule("boolEquals", r.Source, r.Field);
                o["value"] = r.Value;
                return o;
            }
            case Rule.ArrayOf r:
            {
                var o = FieldRule("arrayOf", r.Source, r.Field);
                o["type"] = TypeName(r.ElementType);
                return o;
            }
            case Rule.All r:
                return new JsonObject { ["rule"] = "all", ["rules"] = WriteRules(r.Rules) };
            case Rule.Any r:
                return new JsonObject { ["rule"] = "any", ["rules"] = WriteRules(r.Rules) };
            case Rule.Custom r:
                return new JsonObject { ["rule"] = "custom", ["name"] = r.Name };
            default:
                throw new InvalidOperationException($"Unknown rule {rule.GetType().Name}.");
        }
    }

    private static JsonArray WriteRules(IReadOnlyList<Rule> rules)
    {
        var array = new JsonArray();
        foreach (var rule in rules)
            array.Add(WriteRule(rule));
        return array;
    }

    private static JsonObject FieldRule(string kind, RuleSource source, string field) =>
        new()
        {
            ["rule"] = kind,
            ["source"] = source == RuleSource.Context ? "context" : "input",
            ["field"] = field,
        };

    private static JsonNode WriteReduction(Reduction reduction)
    {
        switch (reduction)
        {
            case Reduction.Keep:
                return new JsonObject { ["reduce"] = "keep" };
            case Reduction.Clear:
                return new JsonObject { ["reduce"] = "clear" };
            case Reduction.Reset:
                return new JsonObject { ["reduce"] = "reset" };
            case Reduction.Custom c:
                return new JsonObject { ["reduce"] = "custom", ["name"] = c.Name };
            case Reduction.Set s:
            {
                var steps = new JsonArray();
                foreach (var step in s.Steps)
                    steps.Add(
                        new JsonObject
                        {
                            ["field"] = step.Field,
                            ["value"] = WriteSource(step.Source),
                        }
                    );
                return new JsonObject { ["reduce"] = "set", ["steps"] = steps };
            }
            default:
                throw new InvalidOperationException(
                    $"Unknown reduction {reduction.GetType().Name}."
                );
        }
    }

    private static JsonObject WriteSource(ValueSource source) =>
        source switch
        {
            ValueSource.FromInput f => new JsonObject { ["input"] = f.Field },
            ValueSource.Constant c => new JsonObject { ["const"] = c.Value?.DeepClone() },
            _ => throw new InvalidOperationException(
                $"Unknown value source {source.GetType().Name}."
            ),
        };

    private static JsonArray ToSortedArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.OrderBy(v => v, StringComparer.Ordinal))
            array.Add(value);
        return array;
    }

    private static string TypeName(JsonFieldType type) => type.ToString().ToLowerInvariant();

    private static string OpName(CompareOp op) =>
        op switch
        {
            CompareOp.GreaterThan => "gt",
            CompareOp.GreaterOrEqual => "gte",
            CompareOp.LessThan => "lt",
            CompareOp.LessOrEqual => "lte",
            CompareOp.EqualTo => "eq",
            _ => throw new InvalidOperationException($"Unknown operator {op}."),
        };
}
