using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Exports a declaratively-authored machine to the neutral IR (the formalized machine.json): identity,
/// structure, per-state context schema, and per-transition guard/reducer as DATA, plus committed states and
/// effect bindings. This is the C# side of the source-and-oracle inversion, everything a generator needs to
/// emit a per-language runtime lives here, exported from the one C# source. Output is canonical JSON so it is
/// a stable golden.
/// </summary>
public static class IrExporter
{
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

        var context = new JsonObject();
        foreach (var (state, schema) in declarative.ContextSchemas)
            context[state.ToString()!] = WriteSchema(schema);

        var inputs = new JsonObject();
        foreach (var (trigger, schema) in declarative.TriggerInputs)
            inputs[trigger.ToString()!] = WriteSchema(schema);

        var transitions = BuildTransitions(machine, declarative);

        var ir = new JsonObject
        {
            ["id"] = def.Id,
            ["version"] = def.Version,
            ["initialState"] = def.InitialState.ToString(),
            ["states"] = ToSortedArray(Enum.GetNames<TState>()),
            ["triggers"] = ToSortedArray(Enum.GetNames<TTrigger>()),
            ["committedStates"] = ToSortedArray(
                machine.CommittedStates.Select(s => s.ToString()!).ToArray()
            ),
            ["context"] = context,
            ["inputs"] = inputs,
            ["transitions"] = transitions,
        };

        return CanonicalJson.Serialize(ir);
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
            if (effect is not null)
                obj["effect"] = new JsonObject
                {
                    ["type"] = effect.EffectType.FullName,
                    ["keyPrefix"] = effect.KeyPrefix,
                };

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

    private static JsonObject WriteSchema(ContextSchema schema)
    {
        var fields = new JsonArray();
        foreach (var field in schema.Fields)
        {
            var constraints = new JsonArray();
            foreach (var constraint in field.Constraints)
                constraints.Add(WriteRule(constraint));

            fields.Add(
                new JsonObject
                {
                    ["name"] = field.Name,
                    ["type"] = TypeName(field.Type),
                    ["nullable"] = field.Nullable,
                    ["constraints"] = constraints,
                }
            );
        }
        return new JsonObject { ["fields"] = fields };
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
