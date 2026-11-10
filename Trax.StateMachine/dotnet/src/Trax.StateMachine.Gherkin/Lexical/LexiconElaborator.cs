using System.Text.Json.Nodes;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// The fields of a train's input and output, as the host reads them from its records: each a field in the IR's shape,
/// sorted by name. Null fields mean the train was not found, with <see cref="Problem"/> saying why.
/// </summary>
internal sealed record TrainShape(JsonArray? Input, JsonArray? Output, string? Problem);

/// <summary>
/// Turns a parsed lexicon into the glossary it means (<c>conformance/glossary.schema.json</c>, version 1), filling in
/// what the lexicon implies: a machine's noun from its id, its context from what writes it, a train's full name, its
/// input wiring by name, each done outcome's reduction and example. Every key written records the line it came from,
/// so a problem the glossary reader or derivation finds later is reported at that line.
/// </summary>
internal sealed class LexiconElaborator
{
    private const string Owner = "$owner";

    private readonly LxDoc _doc;
    private readonly Func<string, TrainShape>? _trains;
    private readonly List<LexProblem> _problems = [];
    private readonly Dictionary<string, int> _lines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LxConstant> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LxType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LxMachine> _machines = new(StringComparer.Ordinal);

    private LexiconElaborator(LxDoc doc, Func<string, TrainShape>? trains)
    {
        _doc = doc;
        _trains = trains;
    }

    internal static (
        JsonObject Glossary,
        IReadOnlyDictionary<string, int> Lines,
        List<LexProblem> Problems
    ) Elaborate(LxDoc doc, Func<string, TrainShape>? trains)
    {
        var elaborator = new LexiconElaborator(doc, trains);
        var glossary = elaborator.Root();
        return (glossary, elaborator._lines, elaborator._problems);
    }

    private void Problem(int line, string message) => _problems.Add(new LexProblem(line, message));

    private void Map(string pointer, int line)
    {
        if (line > 0)
            _lines[pointer] = line;
    }

    private static string P(string parent, string key) =>
        $"{parent}/{key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    // A name defined twice names both lines.
    private bool Once<T>(
        Dictionary<string, T> seen,
        string name,
        T value,
        int line,
        Func<T, int> lineOf,
        string what
    )
    {
        if (seen.TryGetValue(name, out var earlier))
        {
            Problem(line, $"{what} '{name}' is already defined at line {lineOf(earlier)}.");
            return false;
        }
        seen[name] = value;
        return true;
    }

    private JsonObject Root()
    {
        var root = new JsonObject();
        if (_doc.Version is null)
            Problem(1, "a lexicon starts with 'Version: 1'.");
        else if (_doc.Version != 1)
            Problem(
                _doc.VersionLine,
                $"'Version: {_doc.Version}' is not one this reader reads; it reads version 1."
            );
        root["version"] = 1;
        Map("/version", _doc.VersionLine);

        foreach (var constant in _doc.Constants)
            Once(_constants, constant.Name, constant, constant.Line, c => c.Line, "the constant");
        foreach (var constant in _constants.Values)
            Resolve(constant);
        foreach (var type in _doc.Types)
        {
            if (!Once(_types, type.Name, type, type.Line, t => t.Line, "the type"))
                continue;
            if ((type.Values is null) == (type.Has.Count == 0))
                Problem(
                    type.Line,
                    $"the type '{type.Name}' is a list of values ('Values:') or a record ('Has:' lines), one or the other."
                );
            if (type.Values is { } values)
                for (var i = 0; i < values.Count; i++)
                    if (values.IndexOf(values[i]) != i)
                        Problem(type.ValuesLine, $"'{values[i]}' is one of the values twice.");
        }

        var actors = new JsonObject();
        var actorLines = new Dictionary<string, LxActor>(StringComparer.Ordinal);
        foreach (var actor in _doc.Actors)
        {
            if (!Once(actorLines, actor.Name, actor, actor.Line, a => a.Line, "the actor"))
                continue;
            var at = P("/actors", actor.Name);
            Map(at, actor.Line);
            Map(P(at, "noun"), actor.Line);
            var json = new JsonObject { ["noun"] = actor.Noun };
            if (actor.Roles is not null && actor.AnyLine != 0)
                Problem(
                    Math.Max(actor.AnyLine, actor.RolesLine),
                    "'Any signed-in' and 'Roles:' are refused beside each other: an actor holds roles, or is any signed-in caller."
                );
            else if (actor.Roles is null && actor.AnyLine == 0)
                Problem(
                    actor.Line,
                    $"the actor '{actor.Name}' says who it is: 'Roles: <role>' or 'Any signed-in'."
                );
            if (actor.Roles is { } roles)
            {
                json["roles"] = Strings(roles);
                Map(P(at, "roles"), actor.RolesLine);
            }
            if (actor.AnyLine != 0)
            {
                json["anyAuthenticated"] = true;
                Map(P(at, "anyAuthenticated"), actor.AnyLine);
            }
            actors[actor.Name] = json;
        }
        if (_doc.Actors.Count == 0)
            Problem(0, "a lexicon names at least one actor: 'Actor: <name> \"<noun>\"'.");
        root["actors"] = actors;
        Map("/actors", 1);

        foreach (var machine in _doc.Machines)
            Once(_machines, machine.Id, machine, machine.Line, m => m.Line, "the machine");
        var machines = new JsonObject();
        foreach (var machine in _machines.Values)
            machines[machine.Id] = Machine(machine);
        if (_doc.Machines.Count == 0)
            Problem(0, "a lexicon names at least one machine: 'Machine: <id>'.");
        root["machines"] = machines;
        Map("/machines", 1);

        if (_doc.Views.Count > 0)
        {
            var views = new JsonObject();
            var seen = new Dictionary<string, LxView>(StringComparer.Ordinal);
            foreach (var view in _doc.Views)
                if (Once(seen, view.Name, view, view.Line, v => v.Line, "the view"))
                    views[view.Name] = View(view);
            root["views"] = views;
        }
        return root;
    }

    // Each constant's value with every name in it followed to a value, once. A name that is no constant, or one that
    // comes back round to itself, is a problem at the constant's line, and the constant has no value.
    private readonly Dictionary<string, LxValue?> _resolved = new(StringComparer.Ordinal);

    private LxValue? Resolve(LxConstant constant)
    {
        if (_resolved.TryGetValue(constant.Name, out var done))
            return done;
        var seen = new List<string> { constant.Name };
        var value = constant.Value;
        while (value is LxRef r)
        {
            if (!_constants.TryGetValue(r.Name, out var next))
            {
                Problem(constant.Line, $"'{r.Name}' is not a constant; a text value is quoted.");
                value = null;
                break;
            }
            if (seen.Contains(next.Name))
            {
                Problem(
                    constant.Line,
                    $"'{constant.Name}' refers to itself through {string.Join(", ", seen.Skip(1).Append(next.Name).Select(n => $"'{n}'"))}."
                );
                value = null;
                break;
            }
            seen.Add(next.Name);
            value = next.Value;
        }
        if (value is not null && Contains(value))
        {
            Problem(
                constant.Line,
                $"'{constant.Name}' holds a name in a list; a constant's list holds values."
            );
            value = null;
        }
        _resolved[constant.Name] = value;
        return value;
    }

    private static bool Contains(LxValue value) =>
        value is LxList list && list.Items.Any(i => i is LxRef || Contains(i));

    private static JsonArray Strings(IEnumerable<string> items) =>
        new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

    // ---------------------------------------------------------------- machines

    private sealed record Writer(string Field, JsonObject? Shape, int Line);

    private JsonObject Machine(LxMachine m)
    {
        var at = P("/machines", m.Id);
        Map(at, m.Line);
        var json = new JsonObject();
        var owned = m.OwnedBy is not null;
        var system = m.SystemLine != 0;
        if (owned && system)
            Problem(
                Math.Max(m.OwnedLine, m.SystemLine),
                "'System' and 'Owned by:' are refused beside each other: a machine is owned by actors, or by the system."
            );
        else if (!owned && !system)
            Problem(
                m.Line,
                $"the machine '{m.Id}' says who owns it: 'Owned by: <actor>', or 'System'."
            );
        if (owned)
        {
            json["ownedBy"] = m.OwnedBy!.Count == 1 ? m.OwnedBy[0] : Strings(m.OwnedBy);
            Map(P(at, "ownedBy"), m.OwnedLine);
        }
        else if (system)
        {
            json["system"] = m.KeyedBy is { } keyed
                ? new JsonObject { ["keyedBy"] = Strings(keyed) }
                : m.SystemAt ?? $"{m.Id}/main";
            Map(P(at, "system"), m.SystemLine);
        }
        var reference = m.Noun ?? m.Id.Replace('-', ' ');
        json[owned || !system ? "noun" : "name"] = reference;
        Map(P(at, owned || !system ? "noun" : "name"), m.Line);
        if (m.Version is { } version)
        {
            json["version"] = version;
            Map(P(at, "version"), m.VersionLine);
        }
        if (m.Start is { } start)
        {
            json["start"] = start;
            Map(P(at, "start"), m.StartLine);
        }
        if (m.States.Count == 0)
            Problem(m.Line, $"the machine '{m.Id}' has no 'State:' lines.");
        json["initial"] = m.Initial ?? m.States.FirstOrDefault()?.Name ?? "";
        Map(P(at, "initial"), m.Initial is null ? m.Line : m.InitialLine);

        // Outputs first: what a train returns is what Stores: and the context read.
        var outputs = new Dictionary<LxRuns, (JsonArray Fields, JsonArray? Inputs)>();
        foreach (var runs in m.Runs)
            outputs[runs] = TrainFields(runs);

        var context = Context(m, outputs);
        if (context.Count > 0)
        {
            json["context"] = new JsonArray(context.Select(c => (JsonNode?)c.Json).ToArray());
            Map(P(at, "context"), m.Context.FirstOrDefault()?.Line ?? m.Line);
            for (var i = 0; i < context.Count; i++)
                Map(
                    P(
                        P(at, "context"),
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ),
                    context[i].Line
                );
        }
        var shapes = context.ToDictionary(c => c.Name, c => c.Json, StringComparer.Ordinal);

        if (m.StartsWith is { } starts)
        {
            json["initialContext"] = Assigned(starts, shapes, "a context field", m.StartsWithLine);
            Map(P(at, "initialContext"), m.StartsWithLine);
        }

        var states = new JsonObject();
        var stateLines = new Dictionary<string, LxState>(StringComparer.Ordinal);
        foreach (var state in m.States)
            if (Once(stateLines, state.Name, state, state.Line, s => s.Line, "the state"))
                states[state.Name] = State(P(P(at, "states"), state.Name), state, shapes);
        json["states"] = states;
        Map(P(at, "states"), m.States.FirstOrDefault()?.Line ?? m.Line);

        if (m.Triggers.Count > 0)
        {
            var triggers = new JsonObject();
            var seen = new Dictionary<string, LxTrigger>(StringComparer.Ordinal);
            foreach (var trigger in m.Triggers)
                if (Once(seen, trigger.Name, trigger, trigger.Line, t => t.Line, "the trigger"))
                    triggers[trigger.Name] = Trigger(
                        P(P(at, "triggers"), trigger.Name),
                        trigger,
                        shapes
                    );
            json["triggers"] = triggers;
        }
        if (m.Guards.Count > 0)
        {
            var guards = new JsonObject();
            var seen = new Dictionary<string, LxGuard>(StringComparer.Ordinal);
            foreach (var guard in m.Guards)
                if (Once(seen, guard.Name, guard, guard.Line, g => g.Line, "the guard"))
                    guards[guard.Name] = Guard(P(P(at, "guards"), guard.Name), guard, shapes);
            json["guards"] = guards;
        }
        if (m.Runs.Count > 0)
        {
            var invokes = new JsonObject();
            var seen = new Dictionary<string, LxRuns>(StringComparer.Ordinal);
            var phrases = new Dictionary<string, LxOutcome>(StringComparer.Ordinal);
            foreach (var runs in m.Runs)
            {
                if (!seen.TryGetValue(runs.State, out var earlier))
                {
                    seen[runs.State] = runs;
                    invokes[runs.State] = Invoke(
                        P(P(at, "invokes"), runs.State),
                        m,
                        runs,
                        outputs[runs],
                        shapes,
                        phrases
                    );
                }
                else
                    Problem(
                        runs.Line,
                        $"{runs.State} already runs a train, at line {earlier.Line}; a state runs one."
                    );
            }
            json["invokes"] = invokes;
        }
        if (m.Links.Count > 0)
        {
            var links = new JsonObject();
            var seen = new Dictionary<string, LxLink>(StringComparer.Ordinal);
            foreach (var link in m.Links)
                if (Once(seen, link.Phrase, link, link.Line, l => l.Line, "the link"))
                {
                    var l = P(P(at, "links"), link.Phrase);
                    Map(l, link.Line);
                    links[link.Phrase] = new JsonObject
                    {
                        ["machine"] = link.Machine,
                        ["field"] = link.Field,
                    };
                }
            json["links"] = links;
        }
        if (m.DifferentialLine != 0)
        {
            json["differential"] = true;
            Map(P(at, "differential"), m.DifferentialLine);
        }
        if (m.Rules.Count > 0)
        {
            var rules = new JsonObject();
            var seen = new Dictionary<string, LxHandler>(StringComparer.Ordinal);
            foreach (var rule in m.Rules)
                if (Once(seen, rule.Name, rule, rule.Line, r => r.Line, "the rule"))
                {
                    Map(P(P(at, "rules"), rule.Name), rule.Line);
                    rules[rule.Name] = new JsonObject
                    {
                        ["type"] = rule.Type,
                        ["example"] = rule.RuleExample,
                    };
                }
            json["rules"] = rules;
            Map(P(at, "rules"), m.Rules[0].Line);
        }
        if (m.Reductions.Count > 0)
        {
            var reductions = new JsonObject();
            var seen = new Dictionary<string, LxHandler>(StringComparer.Ordinal);
            foreach (var reduction in m.Reductions)
                if (
                    Once(
                        seen,
                        reduction.Name,
                        reduction,
                        reduction.Line,
                        r => r.Line,
                        "the reduction"
                    )
                )
                {
                    Map(P(P(at, "reductions"), reduction.Name), reduction.Line);
                    reductions[reduction.Name] = new JsonObject
                    {
                        ["type"] = reduction.Type,
                        ["example"] = Assigned(
                            reduction.ReductionExample!,
                            shapes,
                            "a context field",
                            reduction.Line
                        ),
                    };
                }
            json["reductions"] = reductions;
            Map(P(at, "reductions"), m.Reductions[0].Line);
        }
        if (m.Migrations.Count > 0)
        {
            var migrations = new JsonObject();
            var seen = new Dictionary<string, LxMigration>(StringComparer.Ordinal);
            foreach (var migration in m.Migrations.OrderBy(x => x.From))
            {
                var from = migration.From.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                if (migration.From < 1)
                {
                    Problem(migration.Line, "a migration steps from version 1 or later.");
                    continue;
                }
                if (
                    Once(
                        seen,
                        from,
                        migration,
                        migration.Line,
                        x => x.Line,
                        "the migration from version"
                    )
                )
                {
                    Map(P(P(at, "migrations"), from), migration.Line);
                    migrations[from] = migration.Type;
                }
            }
            json["migrations"] = migrations;
            Map(P(at, "migrations"), m.Migrations[0].Line);
        }
        if (m.Rules.Count == 0)
            Map(P(at, "rules"), m.Line);
        if (m.Reductions.Count == 0)
            Map(P(at, "reductions"), m.Line);
        if (m.Migrations.Count == 0)
            Map(P(at, "migrations"), m.VersionLine == 0 ? m.Line : m.VersionLine);
        return json;
    }

    private sealed record ContextField(string Name, JsonObject Json, int Line);

    // The machine's context: each field a Context: line declares, in the order written, then each field something
    // writes and no line declares, sorted by name. A field is non-null unless a Context: line says it may be empty.
    private List<ContextField> Context(
        LxMachine m,
        Dictionary<LxRuns, (JsonArray Fields, JsonArray? Inputs)> outputs
    )
    {
        var declared = new Dictionary<string, (LxField Field, JsonObject Json)>(
            StringComparer.Ordinal
        );
        var partial = new Dictionary<string, LxField>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var field in m.Context)
        {
            if (declared.TryGetValue(field.Name, out var d) || partial.ContainsKey(field.Name))
            {
                var line = declared.TryGetValue(field.Name, out d)
                    ? d.Field.Line
                    : partial[field.Name].Line;
                Problem(
                    field.Line,
                    $"the context field '{field.Name}' is already given at line {line}."
                );
                continue;
            }
            if (field.Type is null)
                partial[field.Name] = field;
            else
            {
                declared[field.Name] = (field, Field(field, "context"));
                order.Add(field.Name);
            }
        }

        // What each write says the field is.
        var writers = new List<Writer>();
        foreach (var trigger in m.Triggers)
        {
            if (trigger.Reduce?.Steps is not { } steps)
                continue;
            var inputs = trigger.Input.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);
            foreach (var set in steps)
                writers.Add(
                    new Writer(
                        set.Field,
                        set.Value is LxRef r && inputs.TryGetValue(r.Name, out var input)
                            ? Field(input, "input")
                            : LiteralShape(set.Value),
                        set.Line
                    )
                );
        }
        foreach (var runs in m.Runs)
        {
            var output = outputs[runs].Fields;
            // A store of something the train does not return is reported once, where the invoke is read.
            foreach (var store in runs.Stores.Where(s => OutputField(output, s.Output) is not null))
                writers.Add(
                    new Writer(store.Context, OutputField(output, store.Output), store.Line)
                );
            foreach (var set in runs.Sets)
                writers.Add(
                    new Writer(
                        set.Field,
                        set.Value is LxRef r && OutputField(output, r.Name) is { } shape
                            ? shape
                            : LiteralShape(set.Value),
                        set.Line
                    )
                );
        }

        var inferred = new Dictionary<string, (JsonObject? Shape, int Line)>(
            StringComparer.Ordinal
        );
        foreach (var writer in writers)
        {
            if (declared.TryGetValue(writer.Field, out var d))
            {
                if (writer.Shape is { } shape && !SameType(d.Json, shape))
                    Problem(
                        writer.Line,
                        $"'{writer.Field}' is declared {Describe(d.Json)} at line {d.Field.Line}, and this writes {Describe(shape)}."
                    );
                continue;
            }
            if (!inferred.TryGetValue(writer.Field, out var known) || known.Shape is null)
                inferred[writer.Field] = (writer.Shape, known.Line == 0 ? writer.Line : known.Line);
            else if (writer.Shape is { } shape && !SameType(known.Shape, shape))
                Problem(
                    writer.Line,
                    $"'{writer.Field}' is {Describe(known.Shape)} where line {known.Line} writes it, and this writes {Describe(shape)}."
                );
        }

        var fields = order
            .Select(n => new ContextField(n, declared[n].Json, declared[n].Field.Line))
            .ToList();
        foreach (
            var (name, (shape, line)) in inferred.OrderBy(kv => kv.Key, StringComparer.Ordinal)
        )
        {
            if (shape is null)
            {
                Problem(
                    line,
                    $"nothing says what '{name}' is: declare it, 'Context: {name} is a <type>'."
                );
                continue;
            }
            var json = new JsonObject { ["name"] = name };
            foreach (var (key, value) in shape)
                if (key is not ("name" or "nullable" or "constraints" or "minimum" or "maximum"))
                    json[key] = value?.DeepClone();
            json["nullable"] = partial.ContainsKey(name);
            json["constraints"] = new JsonArray();
            fields.Add(
                new ContextField(name, json, partial.TryGetValue(name, out var p) ? p.Line : line)
            );
        }
        foreach (var (name, field) in partial)
            if (!inferred.ContainsKey(name))
                Problem(
                    field.Line,
                    $"nothing writes '{name}', so declare what it is: 'Context: {name} is a <type>, may be empty'."
                );
        return fields;
    }

    private static JsonObject? OutputField(JsonArray output, string name) =>
        output.OfType<JsonObject>().FirstOrDefault(f => f["name"]?.GetValue<string>() == name);

    // A Returns: field agrees with the record's when it has the same type, format, nullability and values. The record's
    // numeric bounds are its CLR type's, which no lexicon restates, so they are not compared.
    private static bool SameField(JsonObject written, JsonObject reflected) =>
        SameType(written, reflected)
        && (written["nullable"]?.GetValue<bool>() ?? false)
            == (reflected["nullable"]?.GetValue<bool>() ?? false)
        && Values(written).SetEquals(Values(reflected));

    private static HashSet<string> Values(JsonObject shape) =>
        (shape["enum"] as JsonArray ?? [])
            .Select(v => v!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

    private static string Said(JsonObject shape) =>
        Describe(shape)
        + (shape["nullable"]?.GetValue<bool>() == true ? ", may be empty" : "")
        + (
            shape["enum"] is JsonArray values
                ? ", one of " + string.Join(", ", values.Select(v => $"\"{v}\""))
                : ""
        );

    private static bool SameType(JsonObject a, JsonObject b) =>
        a["type"]?.GetValue<string>() == b["type"]?.GetValue<string>()
        && a["format"]?.GetValue<string>() == b["format"]?.GetValue<string>();

    private static string Describe(JsonObject shape)
    {
        var type = shape["type"]?.GetValue<string>();
        var format = shape["format"]?.GetValue<string>();
        return format is not null ? $"a {format}"
            : type is "integer" or "array" or "object"
                ? $"an {(type == "array" ? "list" : type)}".Replace(
                    "an list",
                    "a list",
                    StringComparison.Ordinal
                )
            : $"a {type}";
    }

    // The shape a literal says its field has, or null when it says nothing (nothing, an empty list, a list of mixed
    // items). A constant says what its value says.
    private JsonObject? LiteralShape(LxValue value)
    {
        switch (value)
        {
            case LxText:
                return new JsonObject { ["type"] = "string" };
            case LxBool:
                return new JsonObject { ["type"] = "boolean" };
            case LxNumber n:
                return new JsonObject
                {
                    ["type"] = double.IsInteger(n.Number) ? "integer" : "number",
                };
            case LxList { Items.Count: > 0 } list when list.Items.All(i => i is LxText):
                return new JsonObject
                {
                    ["type"] = "array",
                    ["values"] = new JsonObject { ["type"] = "string", ["nullable"] = false },
                };
            case LxRef r when _constants.TryGetValue(r.Name, out var constant):
                return Resolve(constant) is { } resolved ? LiteralShape(resolved) : null;
            default:
                return null;
        }
    }

    private JsonNode State(string at, LxState state, Dictionary<string, JsonObject> shapes)
    {
        Map(at, state.Line);
        Map(P(at, "phrase"), state.Line);
        if (!state.Committed && state.Requires is null && state.Context is null)
            return JsonValue.Create(state.Phrase)!;
        var json = new JsonObject { ["phrase"] = state.Phrase };
        if (state.Committed)
        {
            json["committed"] = true;
            Map(P(at, "committed"), state.Line);
        }
        Dictionary<string, JsonObject> own = shapes;
        if (state.Context is { } context)
        {
            var seen = new Dictionary<string, LxField>(StringComparer.Ordinal);
            var fields = new JsonArray();
            own = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var field in context)
                if (Once(seen, field.Name, field, field.Line, f => f.Line, "the context field"))
                {
                    var json1 = Field(field, "context");
                    Map(
                        P(
                            P(at, "context"),
                            fields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ),
                        field.Line
                    );
                    fields.Add(json1);
                    own[field.Name] = json1;
                }
            json["context"] = fields;
            Map(P(at, "context"), context[0].Line);
        }
        if (state.Requires is { } requires)
        {
            json["requires"] = Rule(requires, "context", own, null);
            Map(P(at, "requires"), requires.Line);
        }
        return json;
    }

    private JsonObject Trigger(string at, LxTrigger trigger, Dictionary<string, JsonObject> context)
    {
        Map(at, trigger.Line);
        Map(P(at, "phrase"), trigger.Line);
        var json = new JsonObject { ["phrase"] = trigger.Phrase };
        var inputs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (trigger.Input.Count > 0)
        {
            var fields = new JsonArray();
            var seen = new Dictionary<string, LxField>(StringComparer.Ordinal);
            foreach (var field in trigger.Input)
                if (Once(seen, field.Name, field, field.Line, f => f.Line, "the input field"))
                {
                    var f = Field(field, "input");
                    Map(
                        P(
                            P(at, "input"),
                            fields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ),
                        field.Line
                    );
                    fields.Add(f);
                    inputs[field.Name] = f;
                }
            json["input"] = fields;
            Map(P(at, "input"), trigger.Input[0].Line);
        }
        if (trigger.Reduce is { } reduce)
        {
            json["reduce"] = Reduction(reduce, inputs, context, "the trigger's input");
            Map(P(at, "reduce"), reduce.Line);
        }
        if (trigger.Effect is { } effect)
        {
            var e = new JsonObject { ["type"] = effect.Type };
            if (effect.KeyPrefix is not null)
                e["keyPrefix"] = effect.KeyPrefix;
            e["example"] = effect.Example;
            json["effect"] = e;
            Map(P(at, "effect"), effect.Line);
        }
        if (trigger.Guard is { } guard)
        {
            json["guard"] = guard;
            Map(P(at, "guard"), trigger.GuardLine);
        }
        return json;
    }

    // A reduction: set steps from a field of the source (the trigger's input, the train's output), a constant or a
    // value typed by the context field it sets; or keep, clear, reset or a custom one.
    private JsonObject Reduction(
        LxReduce reduce,
        Dictionary<string, JsonObject> source,
        Dictionary<string, JsonObject> context,
        string what
    )
    {
        if (reduce.Kind == "custom")
            return new JsonObject { ["reduce"] = "custom", ["name"] = reduce.Name };
        if (reduce.Steps is not { } sets)
            return new JsonObject { ["reduce"] = reduce.Kind };
        return new JsonObject
        {
            ["reduce"] = "set",
            ["steps"] = Steps(sets, source, context, what),
        };
    }

    private JsonArray Steps(
        IEnumerable<LxSet> sets,
        Dictionary<string, JsonObject> source,
        Dictionary<string, JsonObject> context,
        string what
    )
    {
        var steps = new JsonArray();
        foreach (var set in sets)
        {
            JsonObject value;
            if (set.Value is LxRef r && source.ContainsKey(r.Name))
                value = new JsonObject { ["input"] = r.Name };
            else if (set.Value is LxRef unknown && !_constants.ContainsKey(unknown.Name))
            {
                Problem(set.Line, $"'{unknown.Name}' is not a field of {what}, nor a constant.");
                continue;
            }
            else
                value = new JsonObject
                {
                    ["const"] = Typed(set.Value, context.GetValueOrDefault(set.Field), set.Field),
                };
            steps.Add(new JsonObject { ["field"] = set.Field, ["value"] = value });
        }
        return steps;
    }

    private JsonObject Guard(string at, LxGuard guard, Dictionary<string, JsonObject> context)
    {
        Map(at, guard.Line);
        Map(P(at, "rule"), guard.Line);
        var json = new JsonObject();
        if (guard.Holds is not null)
        {
            json["holds"] = guard.Holds;
            Map(P(at, "holds"), guard.HoldsLine);
        }
        if (guard.Fails is not null)
        {
            json["fails"] = guard.Fails;
            Map(P(at, "fails"), guard.FailsLine);
        }
        json["rule"] = Rule(guard.Rule, "context", context, null);
        return json;
    }

    // ---------------------------------------------------------------- trains

    private readonly Dictionary<LxRuns, string> _trainNames = [];

    private string TrainName(LxRuns runs)
    {
        if (!_trainNames.TryGetValue(runs, out var name))
            _trainNames[runs] = name = FullTrainName(runs);
        return name;
    }

    private string FullTrainName(LxRuns runs)
    {
        if (runs.Train.Contains('.', StringComparison.Ordinal))
            return runs.Train;
        if (_doc.TrainsIn is null)
        {
            Problem(
                runs.Line,
                $"'Runs: {runs.Train}' names a train without its namespace: write its full name, or give the namespace once on a 'Trains in:' line."
            );
            return runs.Train;
        }
        return $"{_doc.TrainsIn}.I{runs.Train}Train";
    }

    // The train's output fields, from its record when the assemblies were given, else from Returns:; and its input
    // fields when the record says them.
    private (JsonArray Fields, JsonArray? Inputs) TrainFields(LxRuns runs)
    {
        var name = TrainName(runs);
        JsonArray? written = null;
        if (runs.Returns is { } returns)
        {
            var seen = new Dictionary<string, LxField>(StringComparer.Ordinal);
            written = new JsonArray(
                returns
                    .Where(f => Once(seen, f.Name, f, f.Line, x => x.Line, "the output field"))
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => (JsonNode?)Field(f, "input"))
                    .ToArray()
            );
        }
        if (_trains is null)
            return (written ?? [], null);
        var shape = _trains(name);
        if (shape.Problem is { } problem)
        {
            Problem(runs.Line, problem);
            return (written ?? [], null);
        }
        var output = shape.Output!;
        if (written is not null)
        {
            var record = output
                .OfType<JsonObject>()
                .ToDictionary(f => f["name"]!.GetValue<string>(), StringComparer.Ordinal);
            foreach (var field in written.OfType<JsonObject>())
            {
                var n = field["name"]!.GetValue<string>();
                if (!record.TryGetValue(n, out var reflected))
                    Problem(
                        runs.ReturnsLine,
                        $"the train {name} returns no '{n}'; its output has {Names(record.Keys)}."
                    );
                else if (!SameField(field, reflected))
                    Problem(
                        runs.ReturnsLine,
                        $"the train {name} returns '{n}' as {Said(reflected)}, not {Said(field)}."
                    );
            }
            foreach (
                var n in record.Keys.Where(k =>
                    !written.OfType<JsonObject>().Any(f => f["name"]!.GetValue<string>() == k)
                )
            )
                Problem(
                    runs.ReturnsLine,
                    $"the train {name} also returns '{n}', which 'Returns:' leaves out."
                );
            // A Returns: line that agrees with the record is what is written, so the glossary is the same with the
            // assemblies and without them.
            return (written, shape.Input);
        }
        return ((JsonArray)output.DeepClone(), shape.Input);
    }

    private static string Names(IEnumerable<string> names)
    {
        var list = names.Select(n => $"'{n}'").ToList();
        return list.Count == 0 ? "no fields" : string.Join(", ", list);
    }

    private JsonObject Invoke(
        string at,
        LxMachine m,
        LxRuns runs,
        (JsonArray Fields, JsonArray? Inputs) train,
        Dictionary<string, JsonObject> context,
        Dictionary<string, LxOutcome> phrases
    )
    {
        Map(at, runs.Line);
        Map(P(at, "train"), runs.Line);
        var json = new JsonObject { ["train"] = TrainName(runs) };
        var allContext = m
            .States.SelectMany(s => s.Context ?? [])
            .Select(f => f.Name)
            .Concat(context.Keys)
            .ToHashSet(StringComparer.Ordinal);

        // The input: each field written, in the order written, then each the record has and no line writes, from the
        // context field of its name or, for 'owner', from the owner.
        var input = new JsonObject();
        var inputLines = new Dictionary<string, LxInput>(StringComparer.Ordinal);
        foreach (var item in runs.Inputs)
        {
            if (!Once(inputLines, item.Field, item, item.Line, x => x.Line, "the train input"))
                continue;
            var from = item.From == item.Field && item.Field == "owner" ? Owner : item.From;
            input[item.Field] = from;
            Map(P(P(at, "input"), item.Field), item.Line);
        }
        if (train.Inputs is { } record)
        {
            var names = record
                .OfType<JsonObject>()
                .Select(f => f["name"]!.GetValue<string>())
                .ToList();
            foreach (var item in runs.Inputs.Where(i => !names.Contains(i.Field)))
                Problem(
                    item.Line,
                    $"the train {json["train"]} takes no '{item.Field}'; its input has {Names(names)}."
                );
            foreach (var name in names.Where(n => !input.ContainsKey(n)))
            {
                if (name == "owner")
                    input[name] = Owner;
                else if (allContext.Contains(name))
                    input[name] = name;
                else
                {
                    Problem(
                        runs.Line,
                        $"the train input '{name}' has no context field to come from: write 'Input: {name} from <field>'."
                    );
                    continue;
                }
                Map(P(P(at, "input"), name), runs.Line);
            }
        }
        if (input.Count > 0)
        {
            json["input"] = input;
            Map(P(at, "input"), runs.Inputs.FirstOrDefault()?.Line ?? runs.Line);
        }

        var output = train.Fields;
        var outputs = output
            .OfType<JsonObject>()
            .ToDictionary(f => f["name"]!.GetValue<string>(), StringComparer.Ordinal);
        if (output.Count > 0)
        {
            json["output"] = output;
            Map(P(at, "output"), runs.Returns is null ? runs.Line : runs.ReturnsLine);
        }

        // What every done outcome stores: Stores: and Sets: lines, in the order written.
        foreach (var store in runs.Stores.Where(s => !outputs.ContainsKey(s.Output)))
            Problem(
                store.Line,
                $"the train {json["train"]} returns no '{store.Output}'"
                    + (
                        runs.Returns is null && _trains is null
                            ? ": say what it returns, 'Returns: <field> is a <type>'."
                            : $"; it returns {Names(outputs.Keys)}."
                    )
            );
        var steps = new List<(int Line, int Order, LxSet Set)>();
        var order = 0;
        foreach (var store in runs.Stores.Where(s => outputs.ContainsKey(s.Output)))
            steps.Add(
                (
                    store.Line,
                    order++,
                    new LxSet(store.Context, new LxRef(store.Output, store.Line), store.Line)
                )
            );
        foreach (var set in runs.Sets)
            steps.Add((set.Line, order++, set));
        var reduce =
            steps.Count == 0
                ? null
                : Steps(
                    steps.OrderBy(s => s.Line).ThenBy(s => s.Order).Select(s => s.Set),
                    outputs,
                    context,
                    "the train's output"
                );

        var example = new Dictionary<string, LxAssign>(StringComparer.Ordinal);
        foreach (var assign in runs.Example)
        {
            if (!outputs.ContainsKey(assign.Field))
                Problem(
                    assign.Line,
                    $"'{assign.Field}' is not one of what the train returns ({Names(outputs.Keys)})."
                );
            else
                Once(example, assign.Field, assign, assign.Line, a => a.Line, "the example of");
        }

        var outcomes = new JsonObject();
        var examples = new JsonObject();
        var own = new Dictionary<string, LxOutcome>(StringComparer.Ordinal);
        foreach (var outcome in runs.Outcomes)
        {
            if (!Once(own, outcome.Phrase, outcome, outcome.Line, o => o.Line, "the outcome"))
                continue;
            if (phrases.TryGetValue(outcome.Phrase, out var other))
            {
                Problem(
                    outcome.Line,
                    $"the outcome '{outcome.Phrase}' is already defined at line {other.Line}."
                );
                continue;
            }
            phrases[outcome.Phrase] = outcome;
            var o = P(P(at, "outcomes"), outcome.Phrase);
            Map(o, outcome.Line);
            var json1 = new JsonObject { ["outcome"] = outcome.Kind };
            if (outcome.When is { } when)
            {
                json1["when"] = Rule(when, "input", outputs, "what the train returns");
                Map(P(o, "when"), outcome.Line);
            }
            if (outcome.Kind == "done" && reduce is not null)
            {
                json1["reduce"] = new JsonObject
                {
                    ["reduce"] = "set",
                    ["steps"] = reduce.DeepClone(),
                };
                Map(P(o, "reduce"), runs.Stores.FirstOrDefault()?.Line ?? runs.Sets[0].Line);
            }
            outcomes[outcome.Phrase] = json1;

            if (outcome.Kind != "done" || output.Count == 0)
                continue;
            var given = Equalities(outcome.When);
            var values = new JsonObject();
            foreach (var (name, shape) in outputs)
            {
                if (
                    given.TryGetValue(name, out var value)
                    || (
                        example.TryGetValue(name, out var assign)
                        && (value = assign.Value) is not null
                    )
                )
                    values[name] = Typed(value, shape, name);
                else
                    Problem(
                        outcome.Line,
                        $"the example of '{outcome.Phrase}' has no value for '{name}': give it on an 'Example:' line."
                    );
            }
            examples[outcome.Phrase] = values;
            Map(P(P(at, "examples"), outcome.Phrase), outcome.Line);
        }
        if (runs.Outcomes.Count == 0)
            Problem(
                runs.Line,
                $"{runs.State} runs a train and says none of its outcomes: 'Outcome: \"<phrase>\" when …'."
            );
        json["outcomes"] = outcomes;
        Map(P(at, "outcomes"), runs.Outcomes.FirstOrDefault()?.Line ?? runs.Line);
        if (examples.Count > 0)
        {
            json["examples"] = examples;
            Map(P(at, "examples"), runs.Example.FirstOrDefault()?.Line ?? runs.Line);
        }
        return json;
    }

    // The values a done outcome's rule pins: each 'field is value' it joins with 'and', or its one atom.
    private static Dictionary<string, LxValue> Equalities(LxRule? when)
    {
        var given = new Dictionary<string, LxValue>(StringComparer.Ordinal);
        if (when is null || when.Connective == "or")
            return given;
        foreach (var atom in when.Atoms)
            if (atom.Source is null or "input")
            {
                if (atom.Kind == "equals")
                    given[atom.Field] = atom.Value!;
                else if (atom.Kind == "compare" && atom.Op == "eq")
                    given[atom.Field] = atom.Value!;
            }
        return given;
    }

    // ---------------------------------------------------------------- views

    private JsonObject View(LxView view)
    {
        var at = P("/views", view.Name);
        Map(at, view.Line);
        var json = new JsonObject();
        var reads = new JsonArray();
        if (view.Reads is null)
            Problem(view.Line, $"the view '{view.Name}' says what it reads: 'Reads: <machine>'.");
        foreach (var machine in view.Reads ?? [])
        {
            var read = new JsonObject { ["machine"] = machine };
            // A system machine at a fixed key is read at it; nothing else has a key to read.
            if (
                _machines.TryGetValue(machine, out var m)
                && m.SystemLine != 0
                && m.KeyedBy is null
                && m.OwnedBy is null
            )
                read["key"] = m.SystemAt ?? $"{m.Id}/main";
            Map(
                P(
                    P(at, "reads"),
                    reads.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                view.ReadsLine
            );
            reads.Add(read);
        }
        json["reads"] = reads;
        Map(P(at, "reads"), view.ReadsLine == 0 ? view.Line : view.ReadsLine);
        var fields = new JsonObject();
        var seen = new Dictionary<string, LxViewField>(StringComparer.Ordinal);
        foreach (var field in view.Fields)
        {
            if (!Once(seen, field.Name, field, field.Line, f => f.Line, "the field"))
                continue;
            var f = P(P(at, "fields"), field.Name);
            Map(f, field.Line);
            Map(P(f, "from"), field.Line);
            var json1 = new JsonObject { ["from"] = $"{field.Machine}.{field.Source}" };
            if (field.Labels.Count > 0)
            {
                var labels = new JsonObject();
                var given = new Dictionary<string, LxLabel>(StringComparer.Ordinal);
                foreach (var label in field.Labels)
                    if (Once(given, label.State, label, label.Line, l => l.Line, "the label of"))
                    {
                        labels[label.State] = label.Label;
                        Map(P(P(f, "labels"), label.State), label.Line);
                    }
                json1["labels"] = labels;
                Map(P(f, "labels"), field.Labels[0].Line);
            }
            fields[field.Name] = json1;
        }
        if (view.Fields.Count == 0)
            Problem(
                view.Line,
                $"the view '{view.Name}' shows at least one field: 'Field: <name> from <machine>'s <field>'."
            );
        json["fields"] = fields;
        Map(P(at, "fields"), view.Fields.FirstOrDefault()?.Line ?? view.Line);
        return json;
    }

    // ---------------------------------------------------------------- fields, rules and values

    /// <summary>A field in the IR's shape: name, its type's keys, nullable, constraints, and its clauses.</summary>
    private JsonObject Field(LxField field, string source)
    {
        var json = new JsonObject { ["name"] = field.Name };
        var shape = Shape(field.Type!, field.Line);
        foreach (var (key, value) in shape)
            json[key] = value?.DeepClone();
        json["nullable"] = field.MayBeEmpty;
        json["constraints"] = field.Where is { } where
            ? new JsonArray(Rule(where, source, new() { [field.Name] = json }, null))
            : new JsonArray();
        if (field.Minimum is { } minimum)
            json["minimum"] = Number(minimum);
        if (field.Maximum is { } maximum)
            json["maximum"] = Number(maximum);
        if (field.Enum is { } values)
        {
            if (json["type"]?.GetValue<string>() != "string")
                Problem(
                    field.Line,
                    $"'{field.Name}' is {Describe(shape)}, and only a string is one of a list of values."
                );
            json["enum"] = Strings(values);
        }
        return json;
    }

    // A type's keys, without the field's own (name, nullable, constraints).
    private JsonObject Shape(LxTypeRef type, int line)
    {
        switch (type.Kind)
        {
            case LxTypeRef.List:
            {
                var values = Shape(type.Element!, line);
                values["nullable"] = false;
                return new JsonObject { ["type"] = "array", ["values"] = values };
            }
            case LxTypeRef.Map:
            {
                var values = Shape(type.Element!, line);
                values["nullable"] = false;
                var map = new JsonObject { ["type"] = "object", ["values"] = values };
                // A key that is any string has no shape to check, as the host writes a Dictionary<string, T>.
                var keys = Shape(type.Key!, line);
                if (keys.Count > 1 || keys["type"]?.GetValue<string>() != "string")
                {
                    keys["nullable"] = false;
                    map["keys"] = keys;
                }
                return map;
            }
            case LxTypeRef.Simple:
                return type.Name switch
                {
                    "string" or "number" or "integer" or "boolean" or "object" or "array" =>
                        new JsonObject { ["type"] = type.Name },
                    _ => new JsonObject { ["type"] = "string", ["format"] = type.Name },
                };
        }
        var named = Named(type.Name);
        if (named is null)
        {
            Problem(
                line,
                $"'{type.Name}' is not a type: a string, a number, an integer, a boolean, a uuid, a date, … or a 'Type:' this lexicon names."
            );
            return new JsonObject { ["type"] = "string" };
        }
        if (named.Values is { } enumValues)
            return new JsonObject { ["type"] = "string", ["enum"] = Strings(enumValues) };
        // A record that holds itself, directly or through another, has no end to write.
        if (!_expanding.Add(named.Name))
        {
            Problem(
                line,
                $"'{named.Name}' contains itself, so it has no end: hold an id in its place."
            );
            return new JsonObject { ["type"] = "object" };
        }
        var fields = new JsonArray();
        foreach (var field in named.Has)
            fields.Add(Field(field, "input"));
        _expanding.Remove(named.Name);
        return new JsonObject { ["type"] = "object", ["fields"] = fields };
    }

    private readonly HashSet<string> _expanding = new(StringComparer.Ordinal);

    private LxType? Named(string name)
    {
        if (_types.TryGetValue(name, out var type))
            return type;
        if (
            name.EndsWith("es", StringComparison.Ordinal)
            && _types.TryGetValue(name[..^2], out type)
        )
            return type;
        if (name.EndsWith('s') && _types.TryGetValue(name[..^1], out type))
            return type;
        return null;
    }

    private static JsonValue Number(double number) =>
        double.IsInteger(number) && Math.Abs(number) < 9007199254740992d
            ? JsonValue.Create((long)number)
            : JsonValue.Create(number);

    /// <summary>
    /// A rule in the IR's shape. A field is read from <paramref name="source"/> unless the atom names its own
    /// (<c>input x</c>, <c>context x</c>); a value is checked against the field's shape when it is known.
    /// </summary>
    private JsonObject Rule(
        LxRule rule,
        string source,
        Dictionary<string, JsonObject> fields,
        string? what
    )
    {
        var atoms = rule.Atoms.Select(a => Atom(a, source, fields, what)).ToList();
        if (atoms.Count == 1)
            return atoms[0];
        return new JsonObject
        {
            ["rule"] = rule.Connective == "or" ? "any" : "all",
            ["rules"] = new JsonArray(atoms.Select(a => (JsonNode?)a).ToArray()),
        };
    }

    private JsonObject Atom(
        LxAtom atom,
        string source,
        Dictionary<string, JsonObject> fields,
        string? what
    )
    {
        if (atom.Kind == "custom")
            return new JsonObject { ["rule"] = "custom", ["name"] = atom.Name };
        var from = atom.Source ?? source;
        var shape = from == source ? fields.GetValueOrDefault(atom.Field) : null;
        if (what is not null && from == source && shape is null)
            Problem(atom.Line, $"'{atom.Field}' is not one of {what} ({Names(fields.Keys)}).");
        var json = new JsonObject { ["source"] = from, ["field"] = atom.Field };
        switch (atom.Kind)
        {
            case "equals" when atom.Value is LxBool b:
                if (shape is not null && shape["type"]?.GetValue<string>() != "boolean")
                    Problem(
                        atom.Line,
                        $"'{atom.Field}' is {Describe(shape)}, so it is not true or false."
                    );
                return Leaf("boolEquals", json, ("value", JsonValue.Create(b.Value)));
            case "equals":
                var text = ((LxText)atom.Value!).Text;
                if (shape is not null && shape["type"]?.GetValue<string>() != "string")
                    Problem(
                        atom.Line,
                        $"'{atom.Field}' is {Describe(shape)}, so its value is not quoted."
                    );
                // The IR has no string equality: 'is "hit"' is one of a single value.
                return Leaf("oneOf", json, ("values", Strings([text])));
            case "oneOf":
                return Leaf("oneOf", json, ("values", Strings(atom.Values!)));
            case "ofType" or "arrayOf":
                return Leaf(atom.Kind, json, ("type", JsonValue.Create(atom.Type)));
            case "compare" or "count" or "length":
                var bound = ((LxNumber)atom.Value!).Number;
                if (atom.Kind != "compare" && !double.IsInteger(bound))
                    Problem(atom.Line, "a count or a length is a whole number.");
                if (
                    atom.Kind == "compare"
                    && shape is not null
                    && shape["type"]?.GetValue<string>() is not ("number" or "integer")
                )
                    Problem(
                        atom.Line,
                        $"'{atom.Field}' is {Describe(shape)}, so it is not compared with a number."
                    );
                return Leaf(
                    atom.Kind,
                    json,
                    ("op", JsonValue.Create(atom.Op)),
                    ("value", Number(bound))
                );
            default:
                return Leaf(atom.Kind, json);
        }
    }

    private static JsonObject Leaf(
        string kind,
        JsonObject body,
        params (string Key, JsonNode? Value)[] extra
    )
    {
        var json = new JsonObject { ["rule"] = kind };
        foreach (var (key, value) in body)
            json[key] = value?.DeepClone();
        foreach (var (key, value) in extra)
            json[key] = value;
        return json;
    }

    // field is value, …: each value typed by the field's shape.
    private JsonObject Assigned(
        IEnumerable<LxAssign> assigns,
        Dictionary<string, JsonObject> shapes,
        string what,
        int line
    )
    {
        var json = new JsonObject();
        foreach (var assign in assigns)
        {
            if (json.ContainsKey(assign.Field))
            {
                Problem(assign.Line, $"'{assign.Field}' is given twice.");
                continue;
            }
            if (!shapes.TryGetValue(assign.Field, out var shape))
            {
                Problem(assign.Line, $"'{assign.Field}' is not {what} ({Names(shapes.Keys)}).");
                continue;
            }
            json[assign.Field] = Typed(assign.Value, shape, assign.Field);
        }
        _ = line;
        return json;
    }

    /// <summary>
    /// A value as its field's type reads it: a quoted text for a string, a number for a number, a whole one for an
    /// integer, true or false, a list in brackets for a list, <c>nothing</c> only where the field may be empty. With
    /// no shape, the value as written.
    /// </summary>
    private JsonNode? Typed(LxValue value, JsonObject? shape, string field)
    {
        if (value is LxRef r)
        {
            if (_constants.TryGetValue(r.Name, out var constant))
                return Resolve(constant) is { } resolved ? Typed(resolved, shape, field) : null;
            Problem(r.Line, $"'{r.Name}' is not a constant; a text value is quoted.");
            return null;
        }
        if (shape is null)
            return Untyped(value);
        var type = shape["type"]?.GetValue<string>();
        switch (value)
        {
            case LxNull:
                if (shape["nullable"]?.GetValue<bool>() != true)
                    Problem(
                        value.Line,
                        $"'{field}' may not be empty, so its value is never nothing."
                    );
                return null;
            case LxText t when type == "string":
                var declared = (shape["enum"] as JsonArray)
                    ?.Select(a => a!.GetValue<string>())
                    .FirstOrDefault(a =>
                        string.Equals(a, t.Text, StringComparison.OrdinalIgnoreCase)
                    );
                // Rules compare a value as written, so a value in another case than its declared one would never
                // match one.
                if (declared is not null && declared != t.Text)
                    Problem(
                        value.Line,
                        $"\"{t.Text}\" is \"{declared}\" in the values of '{field}'; write it as declared."
                    );
                else if (
                    shape["enum"] is JsonArray allowed
                    && !allowed.Any(a =>
                        string.Equals(
                            a?.GetValue<string>(),
                            t.Text,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                    Problem(value.Line, $"\"{t.Text}\" is not one of the values of '{field}'.");
                return JsonValue.Create(t.Text);
            case LxNumber n when type == "integer":
                if (!double.IsInteger(n.Number))
                    Problem(
                        value.Line,
                        $"'{field}' is an integer, so its value is a whole number."
                    );
                return Number(n.Number);
            case LxNumber n when type == "number":
                return Number(n.Number);
            case LxBool b when type == "boolean":
                return JsonValue.Create(b.Value);
            case LxList list when type == "array":
                var items = new JsonArray();
                foreach (var item in list.Items)
                    items.Add(Typed(item, shape["values"] as JsonObject, field));
                return items;
        }
        if (type == "object")
        {
            Problem(
                value.Line,
                $"'{field}' is an object, and a lexicon writes no object values: give it with a custom reduction."
            );
            return null;
        }
        Problem(value.Line, $"'{field}' is {Describe(shape)}, so its value is {Expected(type)}.");
        return null;
    }

    private static string Expected(string? type) =>
        type switch
        {
            "string" => "quoted",
            "integer" => "a whole number",
            "number" => "a number",
            "boolean" => "true or false",
            "array" => "a list in brackets",
            _ => "something else",
        };

    private JsonNode? Untyped(LxValue value) =>
        value switch
        {
            LxText t => JsonValue.Create(t.Text),
            LxNumber n => Number(n.Number),
            LxBool b => JsonValue.Create(b.Value),
            LxList l => new JsonArray(l.Items.Select(Untyped).ToArray()),
            _ => null,
        };
}
