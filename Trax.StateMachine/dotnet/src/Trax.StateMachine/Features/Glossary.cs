using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// What the words of a set of features mean (<c>conformance/glossary.schema.json</c>, version 1; ADR
/// <c>Trax.Docs/adr/0058</c>). <see cref="Read"/> accepts exactly what the schema accepts, then refuses a glossary
/// whose names do not agree: an owner that is not an actor, a phrase two things could mean, a placeholder that is no
/// input, a view of something it does not read. Every refusal is a <see cref="FormatException"/> naming the key.
/// </summary>
internal sealed partial record Glossary(
    IReadOnlyDictionary<string, GlossaryActor> Actors,
    IReadOnlyDictionary<string, GlossaryMachine> Machines,
    IReadOnlyDictionary<string, GlossaryView> Views
)
{
    /// <summary>The only version this reader reads.</summary>
    internal const int Version = 1;

    private const string Document = "glossary";

    /// <summary>Reads a glossary from its JSON text.</summary>
    /// <exception cref="FormatException">The text is not a glossary this reader accepts; the message names the key.</exception>
    internal static Glossary Read(string json)
    {
        var at = new At(Document, "");
        var root = At.Parse(json, Document);
        at.Closed(root, "version", "actors", "machines", "views");
        if (at["version"].Number(at.Required(root, "version")) != Version)
            throw at["version"].Fail($"must be {Version}.");

        var actors = Map(at["actors"], at.Required(root, "actors"), 1, Names.WordAt, ReadActor);
        var machines = Map(
            at["machines"],
            at.Required(root, "machines"),
            1,
            Names.MachineIdAt,
            ReadMachine
        );
        var views = root.ContainsKey("views")
            ? Map(at["views"], root["views"], 0, Names.PhraseAt, ReadView)
            : new Dictionary<string, GlossaryView>(StringComparer.Ordinal);

        var glossary = new Glossary(actors, machines, views);
        glossary.Check(at);
        return glossary;
    }

    private static Dictionary<string, T> Map<T>(
        At at,
        JsonNode? node,
        int minProperties,
        Func<At, JsonNode?, string> name,
        Func<At, string, JsonObject, T> read
    )
    {
        var map = at.Object(node);
        if (map.Count < minProperties)
            throw at.Fail($"must hold at least {minProperties}.");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            name(at[key], JsonValue.Create(key));
            result[key] = read(at[key], key, at[key].Object(value));
        }
        return result;
    }

    private static GlossaryActor ReadActor(At at, string name, JsonObject actor)
    {
        at.Closed(actor, "noun", "roles", "anyAuthenticated");
        var noun = Names.PhraseAt(at["noun"], at.Required(actor, "noun"));
        var hasRoles = actor.ContainsKey("roles");
        var any = actor.ContainsKey("anyAuthenticated");
        // A role-less actor would derive rules that admit any signed-in caller, so saying so is required.
        if (hasRoles == any)
            throw at.Fail("must give 'roles', or 'anyAuthenticated': true, and not both.");
        if (any)
            at["anyAuthenticated"].True(actor["anyAuthenticated"]);
        var roles = hasRoles
            ? at["roles"].Distinct(actor["roles"], 1, (a, n) => a.Text(n, 1, 128))
            : [];
        return new GlossaryActor(name, noun, roles, any);
    }

    private static GlossaryMachine ReadMachine(At at, string id, JsonObject machine)
    {
        at.Closed(
            machine,
            "ownedBy",
            "system",
            "noun",
            "name",
            "start",
            "initial",
            "context",
            "states",
            "triggers",
            "guards",
            "invokes",
            "links",
            "version",
            "initialContext",
            "differential"
        );
        var owned = machine.ContainsKey("ownedBy");
        var system = machine.ContainsKey("system");
        if (owned == system)
            throw at.Fail("must give 'ownedBy' an actor, or 'system' a key, and not both.");
        // A user's machine is referred to by its noun and its owner ("Ada's order"), a system machine by its name.
        if (owned && machine.ContainsKey("name"))
            throw at["name"].Fail("is a system machine's; a user's machine has a 'noun'.");
        if (system && machine.ContainsKey("noun"))
            throw at["noun"].Fail("is a user's machine's; a system machine has a 'name'.");
        // One owning actor, or several, each a person of which owns instances of their own.
        IReadOnlyList<string>? ownedBy =
            !owned ? null
            : machine["ownedBy"] is JsonArray
                ? at["ownedBy"].Distinct(machine["ownedBy"], 1, Names.WordAt)
            : [Names.WordAt(at["ownedBy"], machine["ownedBy"])];
        // A system machine is one instance at a fixed key, or one per key host code starts ({ "keyedBy": [...] }).
        string? key = null;
        IReadOnlyList<string>? keyedBy = null;
        if (system && machine["system"] is JsonObject keyed)
            keyedBy = KeyedBy(at["system"], keyed);
        else if (system)
            key = Names.SystemKeyAt(at["system"], machine["system"]);
        var reference = Names.PhraseAt(
            at[owned ? "noun" : "name"],
            at.Required(machine, owned ? "noun" : "name")
        );
        var start = machine.ContainsKey("start")
            ? Names.PhraseAt(at["start"], machine["start"])
            : null;
        var initial = Names.MemberAt(at["initial"], at.Required(machine, "initial"));
        var (context, contextNames) = machine.ContainsKey("context")
            ? IrShapes.Fields(at["context"], machine["context"])
            : ([], []);

        var statesAt = at["states"];
        var given = statesAt.Object(at.Required(machine, "states"));
        if (given.Count == 0)
            throw statesAt.Fail("must hold at least 1.");
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        var shapes = new Dictionary<string, GlossaryState>(StringComparer.Ordinal);
        foreach (var (name, node) in given)
        {
            Names.MemberAt(statesAt[name], JsonValue.Create(name));
            var shape = ReadState(statesAt[name], name, node);
            states[name] = shape.Phrase;
            shapes[name] = shape;
        }
        var version = 1;
        if (machine.ContainsKey("version"))
        {
            var number = at["version"].Integer(machine["version"]);
            if (number < 1 || number > int.MaxValue)
                throw at["version"].Fail($"must be from 1 to {int.MaxValue}.");
            version = (int)number;
        }
        var initialContext = machine.ContainsKey("initialContext")
            ? (JsonObject)at["initialContext"].Object(machine["initialContext"]).DeepClone()
            : null;
        var differential = machine.ContainsKey("differential");
        if (differential)
            at["differential"].True(machine["differential"]);

        var triggers = machine.ContainsKey("triggers")
            ? Map(at["triggers"], machine["triggers"], 0, Names.MemberAt, ReadTrigger)
            : new();
        var guards = machine.ContainsKey("guards")
            ? Map(at["guards"], machine["guards"], 0, Names.WordAt, ReadGuard)
            : new();
        var invokes = machine.ContainsKey("invokes")
            ? Map(at["invokes"], machine["invokes"], 0, Names.MemberAt, ReadInvoke)
            : new();
        var links = machine.ContainsKey("links")
            ? Map(at["links"], machine["links"], 0, Names.PhraseAt, ReadLink)
            : new();

        return new GlossaryMachine(
            id,
            ownedBy,
            key,
            reference,
            start,
            initial,
            context,
            contextNames,
            states,
            triggers,
            guards,
            invokes,
            links
        )
        {
            KeyedBy = keyedBy,
            Version = version,
            InitialContext = initialContext,
            Differential = differential,
            Shapes = shapes,
        };
    }

    // A state is its phrase, or { phrase, committed, requires, context }: committed, the rule its context must meet, and
    // a context of its own in place of the machine's.
    private static GlossaryState ReadState(At at, string name, JsonNode? node)
    {
        if (node is not JsonObject shape)
            return new GlossaryState(name, Names.PhraseAt(at, node), false, null, null, null);
        at.Closed(shape, "phrase", "committed", "requires", "context");
        var phrase = Names.PhraseAt(at["phrase"], at.Required(shape, "phrase"));
        if (shape.ContainsKey("committed"))
            at["committed"].True(shape["committed"]);
        var requires = shape.ContainsKey("requires")
            ? IrShapes.Rule(at["requires"], shape["requires"])
            : null;
        var (context, contextNames) = shape.ContainsKey("context")
            ? IrShapes.Fields(at["context"], shape["context"])
            : ((JsonArray?)null, (List<string>?)null);
        return new GlossaryState(
            name,
            phrase,
            shape.ContainsKey("committed"),
            requires,
            context,
            contextNames
        );
    }

    /// <summary>The most parts a keyed system machine's key has.</summary>
    internal const int MaxKeyParts = 8;

    /// <summary>A keyed system machine's <c>{ "keyedBy": [...] }</c>: the names of its key's parts, in order.</summary>
    internal static IReadOnlyList<string> KeyedBy(At at, JsonObject keyed)
    {
        at.Closed(keyed, "keyedBy");
        var parts = at["keyedBy"].Distinct(at.Required(keyed, "keyedBy"), 1, Names.WordAt);
        if (parts.Count > MaxKeyParts)
            throw at["keyedBy"].Fail($"must hold at most {MaxKeyParts}.");
        return parts;
    }

    private static GlossaryTrigger ReadTrigger(At at, string name, JsonObject trigger)
    {
        at.Closed(trigger, "phrase", "input", "reduce");
        var phrase = Names.PhraseAt(at["phrase"], at.Required(trigger, "phrase"));
        var (input, inputNames) = trigger.ContainsKey("input")
            ? IrShapes.Fields(at["input"], trigger["input"])
            : ([], []);
        var reduce = trigger.ContainsKey("reduce")
            ? IrShapes.Reduction(at["reduce"], trigger["reduce"])
            : null;
        return new GlossaryTrigger(name, phrase, input, inputNames, reduce);
    }

    private static GlossaryGuard ReadGuard(At at, string name, JsonObject guard)
    {
        at.Closed(guard, "holds", "fails", "rule");
        return new GlossaryGuard(
            name,
            Names.PhraseAt(at["holds"], at.Required(guard, "holds")),
            Names.PhraseAt(at["fails"], at.Required(guard, "fails")),
            IrShapes.Rule(at["rule"], at.Required(guard, "rule"))
        );
    }

    private static GlossaryInvoke ReadInvoke(At at, string state, JsonObject invoke)
    {
        at.Closed(invoke, "train", "input", "output", "outcomes", "examples");
        var train = Names.TrainAt(at["train"], at.Required(invoke, "train"));
        var input = new Dictionary<string, string>(StringComparer.Ordinal);
        if (invoke.ContainsKey("input"))
            foreach (var (field, from) in at["input"].Object(invoke["input"]))
            {
                Names.FieldNameAt(at["input"][field], JsonValue.Create(field));
                input[field] = Names.FieldNameAt(at["input"][field], from);
            }
        var (output, outputNames) = invoke.ContainsKey("output")
            ? IrShapes.Fields(at["output"], invoke["output"])
            : ([], []);
        var outcomes = Map(
            at["outcomes"],
            at.Required(invoke, "outcomes"),
            1,
            Names.PhraseAt,
            ReadOutcome
        );
        var examples = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (invoke.ContainsKey("examples"))
            foreach (var (phrase, example) in at["examples"].Object(invoke["examples"]))
            {
                Names.PhraseAt(at["examples"][phrase], JsonValue.Create(phrase));
                examples[phrase] = (JsonObject)at["examples"][phrase].Object(example).DeepClone();
            }
        return new GlossaryInvoke(state, train, input, output, outputNames, outcomes, examples);
    }

    private static GlossaryOutcome ReadOutcome(At at, string phrase, JsonObject outcome)
    {
        at.Closed(outcome, "outcome", "when", "reduce");
        return new GlossaryOutcome(
            phrase,
            at["outcome"].OneOf(at.Required(outcome, "outcome"), "done", "failed", "cancelled"),
            outcome.ContainsKey("when") ? IrShapes.Rule(at["when"], outcome["when"]) : null,
            outcome.ContainsKey("reduce")
                ? IrShapes.Reduction(at["reduce"], outcome["reduce"])
                : null
        );
    }

    private static GlossaryLink ReadLink(At at, string phrase, JsonObject link)
    {
        at.Closed(link, "machine", "field");
        return new GlossaryLink(
            phrase,
            Names.MachineIdAt(at["machine"], at.Required(link, "machine")),
            Names.FieldNameAt(at["field"], at.Required(link, "field"))
        );
    }

    private static GlossaryView ReadView(At at, string name, JsonObject view)
    {
        at.Closed(view, "reads", "fields");
        var (reads, fields) = Features.Views.Read(at, view);
        return new GlossaryView(name, reads, fields);
    }

    // The checks that need more than one key at a time.
    private void Check(At at)
    {
        // Every phrase that makes a step is unique across the glossary, compared as the step it makes: a user machine's
        // {subject} is its noun, so two machines may share "cancels {subject}" and "Ada cancels her order" still means
        // one thing. The words for a machine's states and guards follow a reference to it, so they are unique within
        // the machine. Actor nouns are one namespace, and machine nouns and system names another.
        var steps = new Dictionary<string, string>(StringComparer.Ordinal);
        var nouns = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        void Unique(Dictionary<string, string> seen, At where, string phrase)
        {
            if (!seen.TryAdd(Names.Normal(phrase), where.Pointer))
                throw where.Fail(
                    $"'{phrase}' is also at {seen[Names.Normal(phrase)]}: a step must mean one thing."
                );
        }

        foreach (var actor in Actors.Values)
            Unique(nouns, at["actors"][actor.Name]["noun"], actor.Noun);

        var systemKeys = Machines.ToDictionary(
            m => m.Key,
            m => m.Value.SystemKey,
            StringComparer.Ordinal
        );
        foreach (var machine in Machines.Values)
        {
            var m = at["machines"][machine.Id];
            foreach (var owner in machine.OwnedBy ?? [])
                if (!Actors.ContainsKey(owner))
                    throw m["ownedBy"].Fail($"'{owner}' is not an actor.");
            if (
                machine.SystemKey is { } key
                && !key.StartsWith(machine.Id + "/", StringComparison.Ordinal)
            )
                throw m["system"].Fail($"must start with the machine's id, '{machine.Id}/'.");
            if (machine.IsSystem && machine.Start is not null)
                throw m["start"]
                    .Fail(
                        "a system machine is started by the host (or, keyed, by host code), never by a step."
                    );
            if (!machine.States.ContainsKey(machine.Initial))
                throw m["initial"].Fail($"'{machine.Initial}' is not one of its states.");

            Distinct(m["context"], machine.ContextNames);
            if (machine.AllContextNames.Contains(HostInvoke.Owner))
                throw m["context"]
                    .Fail(
                        $"has a field named '{HostInvoke.Owner}', which a train's input reads as the owner."
                    );
            if (machine.ContextNames.Contains(ViewField.State))
                throw m["context"]
                    .Fail("has a field named 'state', which a view reads as the machine's state.");
            foreach (var shape in machine.Shapes.Values.Where(s => s.ContextNames is not null))
            {
                var c = m["states"][shape.Name]["context"];
                Distinct(c, shape.ContextNames!);
                if (shape.ContextNames!.Contains(ViewField.State))
                    throw c.Fail(
                        "has a field named 'state', which a view reads as the machine's state."
                    );
            }

            Unique(references, ReferenceAt(m, machine), machine.Reference);
            if (machine.Start is { } start)
                Unique(steps, m["start"], start);

            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (state, phrase) in machine.States)
                Unique(own, m["states"][state], phrase);
            foreach (var guard in machine.Guards.Values)
            {
                Unique(own, m["guards"][guard.Name]["holds"], guard.Holds);
                Unique(own, m["guards"][guard.Name]["fails"], guard.Fails);
            }

            foreach (var trigger in machine.Triggers.Values)
            {
                var t = m["triggers"][trigger.Name];
                Unique(steps, t["phrase"], Said(trigger.Phrase, machine));
                Distinct(t["input"], trigger.InputNames);
                if (trigger.InputNames.Contains(SubjectSlot))
                    throw t["input"]
                        .Fail(
                            $"has a field named '{SubjectSlot}', which a phrase uses for the machine."
                        );
                var placeholders = Placeholders(t["phrase"], trigger.Phrase).ToList();
                var subjects = placeholders.Count(p => p == SubjectSlot);
                // A user's instance is named by its owner where {subject} is, a keyed system machine's by its name and
                // key there; a system machine at a fixed key is named by its name.
                if (!machine.IsSystem && subjects != 1)
                    throw t["phrase"]
                        .Fail(
                            $"must say where the {machine.Reference} goes, once, as {{{SubjectSlot}}}: "
                                + $"'places {{{SubjectSlot}}}' is said 'Ada places her {machine.Reference}'."
                        );
                if (machine.KeyedBy is not null && subjects != 1)
                    throw t["phrase"]
                        .Fail(
                            $"must say where the {machine.Reference} and its key go, once, as {{{SubjectSlot}}}: "
                                + $"'{{{SubjectSlot}}} is closed' is said '{machine.Reference} \"T-1\" is closed'."
                        );
                if (machine.SystemKey is not null && subjects != 0)
                    throw t["phrase"]
                        .Fail(
                            $"names a system machine, which is '{machine.Reference}', never {{{SubjectSlot}}}."
                        );
                foreach (var placeholder in placeholders.Where(p => p != SubjectSlot))
                    if (!trigger.InputNames.Contains(placeholder))
                        throw t["phrase"]
                            .Fail($"'{{{placeholder}}}' is not one of the trigger's input fields.");
            }

            foreach (var invoke in machine.Invokes.Values)
            {
                var v = m["invokes"][invoke.State];
                if (!machine.States.ContainsKey(invoke.State))
                    throw v.Fail($"'{invoke.State}' is not one of the machine's states.");
                foreach (var (field, from) in invoke.Input)
                    if (from == HostInvoke.Owner)
                    {
                        // The owner's id is the server's to give, and a system machine has none.
                        if (machine.IsSystem)
                            throw v["input"]
                                [field]
                                .Fail(
                                    $"a system machine has no owner, so nothing is {HostInvoke.Owner}."
                                );
                    }
                    else if (!machine.AllContextNames.Contains(from))
                        throw v["input"]
                            [field]
                            .Fail($"'{from}' is not one of the machine's context fields.");
                Distinct(v["output"], invoke.OutputNames);
                foreach (var outcome in invoke.Outcomes.Values)
                    Unique(steps, v["outcomes"][outcome.Phrase], outcome.Phrase);
                foreach (var phrase in invoke.Examples.Keys)
                {
                    if (!invoke.Outcomes.TryGetValue(phrase, out var outcome))
                        throw v["examples"][phrase].Fail("is not one of the outcomes.");
                    if (outcome.Kind != "done")
                        throw v["examples"]
                            [phrase]
                            .Fail($"is a {outcome.Kind} outcome, which has no output to give.");
                }
            }

            foreach (var link in machine.Links.Values)
            {
                var l = m["links"][link.Phrase];
                if (Placeholders(l, link.Phrase).ToList() is not [SubjectSlot])
                    throw l.Fail(
                        $"must say where the linked instance goes, once, as {{{SubjectSlot}}}: "
                            + $"'the receipt for {{{SubjectSlot}}}'."
                    );
                if (!Machines.TryGetValue(link.Machine, out var linked))
                    throw l["machine"].Fail($"'{link.Machine}' is not a machine.");
                Unique(steps, l, Said(link.Phrase, linked));
                if (linked.IsSystem)
                    throw l["machine"]
                        .Fail(
                            $"'{link.Machine}' is a system machine, reached at its key, never by a link."
                        );
                if (!machine.AllContextNames.Contains(link.Field))
                    throw l["field"]
                        .Fail($"'{link.Field}' is not one of the machine's context fields.");
            }

            // Only a trigger's or a link's phrase holds a placeholder, so a brace anywhere else is a mistake.
            foreach (var (where, phrase) in OtherPhrases(m, machine))
                if (phrase.AsSpan().IndexOfAny('{', '}') >= 0)
                    throw where.Fail(
                        "only a trigger's or a link's phrase may hold a {placeholder}."
                    );

            // Features name their people, so no phrase speaks as one of them.
            foreach (var (where, phrase) in AllPhrases(m, machine))
                FirstPerson(where, phrase);
        }

        foreach (var actor in Actors.Values)
            FirstPerson(at["actors"][actor.Name]["noun"], actor.Noun);

        Features.Views.CheckNames(at["views"], Views.Values.Select(v => (v.Name, v.Reads)));
        foreach (var view in Views.Values)
        {
            var v = at["views"][view.Name];
            Features.Views.Check(v, view.Reads, view.Fields, systemKeys, Keyed());
            foreach (var field in view.Fields)
            {
                var machine = Machines[field.Machine];
                var f = v["fields"][field.Name];
                if (!field.IsState && !machine.AllContextNames.Contains(field.Source))
                    throw f["from"]
                        .Fail($"'{field.Source}' is not a context field of '{machine.Id}'.");
                if (field.Labels is not null)
                    foreach (var state in field.Labels.Keys)
                        if (!machine.States.ContainsKey(state))
                            throw f["labels"]
                                [state]
                                .Fail($"'{state}' is not a state of '{machine.Id}'.");
            }
        }
    }

    private HashSet<string> Keyed() =>
        Machines.Values.Where(m => m.KeyedBy is not null).Select(m => m.Id).ToHashSet();

    /// <summary>Where a phrase names the machine it is about: <c>places {subject}</c>.</summary>
    internal const string SubjectSlot = "subject";

    // A phrase as the step it makes, for comparing: {subject} is the machine's noun.
    private static string Said(string phrase, GlossaryMachine machine) =>
        phrase.Replace($"{{{SubjectSlot}}}", $"{{{machine.Reference}}}", StringComparison.Ordinal);

    private static At ReferenceAt(At m, GlossaryMachine machine) =>
        m[machine.IsSystem ? "name" : "noun"];

    private static readonly HashSet<string> FirstPersonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "i",
        "me",
        "my",
        "mine",
        "myself",
    };

    private static void FirstPerson(At where, string phrase)
    {
        foreach (var word in phrase.Split(' ', '\'', '\u2019', ',', '.', '!', '?'))
            if (FirstPersonWords.Contains(word))
                throw where.Fail(
                    $"'{phrase}' speaks as '{word}'. Features name their people ('Ada places her order'), so a "
                        + "glossary phrase is written without one."
                );
    }

    private static IEnumerable<(At, string)> AllPhrases(At m, GlossaryMachine machine)
    {
        foreach (var other in OtherPhrases(m, machine))
            yield return other;
        foreach (var trigger in machine.Triggers.Values)
            yield return (m["triggers"][trigger.Name]["phrase"], trigger.Phrase);
        foreach (var phrase in machine.Links.Keys)
            yield return (m["links"][phrase], phrase);
    }

    private static IEnumerable<(At, string)> OtherPhrases(At m, GlossaryMachine machine)
    {
        yield return (ReferenceAt(m, machine), machine.Reference);
        if (machine.Start is { } start)
            yield return (m["start"], start);
        foreach (var (state, phrase) in machine.States)
            yield return (m["states"][state], phrase);
        foreach (var guard in machine.Guards.Values)
        {
            yield return (m["guards"][guard.Name]["holds"], guard.Holds);
            yield return (m["guards"][guard.Name]["fails"], guard.Fails);
        }
        foreach (var invoke in machine.Invokes.Values)
        foreach (var phrase in invoke.Outcomes.Keys)
            yield return (m["invokes"][invoke.State]["outcomes"][phrase], phrase);
    }

    private static void Distinct(At at, IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
            if (!seen.Add(names[i]))
                throw at[i]["name"].Fail($"repeats the field '{names[i]}'.");
    }

    /// <summary>The <c>{name}</c> placeholders of a trigger's phrase, refusing a brace that does not close one.</summary>
    internal static IEnumerable<string> Placeholders(At at, string phrase)
    {
        var names = new List<string>();
        var rest = phrase;
        while (true)
        {
            var open = rest.IndexOf('{');
            var close = rest.IndexOf('}');
            if (open < 0 && close < 0)
                return names;
            if (open < 0 || close < open)
                throw at.Fail("has a '}' that closes nothing.");
            var end = rest.IndexOf('}', open);
            if (end < 0)
                throw at.Fail("has a '{' that is never closed.");
            var name = rest[(open + 1)..end];
            if (!Placeholder().IsMatch(name))
                throw at.Fail($"'{{{name}}}' is not a placeholder: a field name in braces.");
            names.Add(name);
            rest = rest[(end + 1)..];
        }
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

/// <summary>
/// An actor, with the noun a feature names a person by (<c>Given Ada is a customer</c>), and its roles or any signed-in
/// caller.
/// </summary>
internal sealed record GlossaryActor(
    string Name,
    string Noun,
    IReadOnlyList<string> Roles,
    bool AnyAuthenticated
);

/// <summary>
/// One machine of a glossary, owned by the actors in <see cref="OwnedBy"/> (one or more) or the system's.
/// <see cref="Reference"/> is a user's machine's noun (<c>order</c>, said as
/// <c>Ada's order</c>) or a system machine's name (<c>the shop</c>).
/// </summary>
internal sealed record GlossaryMachine(
    string Id,
    IReadOnlyList<string>? OwnedBy,
    string? SystemKey,
    string Reference,
    string? Start,
    string Initial,
    JsonArray Context,
    IReadOnlyList<string> ContextNames,
    IReadOnlyDictionary<string, string> States,
    IReadOnlyDictionary<string, GlossaryTrigger> Triggers,
    IReadOnlyDictionary<string, GlossaryGuard> Guards,
    IReadOnlyDictionary<string, GlossaryInvoke> Invokes,
    IReadOnlyDictionary<string, GlossaryLink> Links
)
{
    /// <summary>
    /// A keyed system machine's key parts by name: one instance per key, started and moved by host code and never by
    /// the host at startup. Null for a user's machine and for a system machine at a fixed <see cref="SystemKey"/>.
    /// </summary>
    internal IReadOnlyList<string>? KeyedBy { get; init; }

    /// <summary>Whether the system owns the machine's instances, at a fixed key or keyed.</summary>
    internal bool IsSystem => SystemKey is not null || KeyedBy is not null;

    /// <summary>The machine's definition version, which its snapshots carry and migrations step from. Defaults to 1.</summary>
    internal int Version { get; init; } = 1;

    /// <summary>The context a new instance starts with, when the glossary gives one; else each field's zero.</summary>
    internal JsonObject? InitialContext { get; init; }

    /// <summary>Whether derivation writes the IR's differential block from the scenarios' walks.</summary>
    internal bool Differential { get; init; }

    /// <summary>Each state's shape: its phrase, whether it is committed, its rule and its own context.</summary>
    internal IReadOnlyDictionary<string, GlossaryState> Shapes { get; init; } =
        new Dictionary<string, GlossaryState>(StringComparer.Ordinal);

    /// <summary>A state's context fields: its own, or the machine's.</summary>
    internal JsonArray ContextOf(string state) =>
        Shapes.TryGetValue(state, out var shape) && shape.Context is { } own ? own : Context;

    /// <summary>Every context field name of the machine, its own and its states'.</summary>
    internal IReadOnlySet<string> AllContextNames =>
        ContextNames
            .Concat(Shapes.Values.SelectMany(s => s.ContextNames ?? []))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The type of a context field, from the machine's context and then its states' in name order, or null when no
    /// context has it.
    /// </summary>
    internal string? FieldType(string field) =>
        Context
            .Concat(
                Shapes
                    .Values.OrderBy(s => s.Name, StringComparer.Ordinal)
                    .SelectMany(s => s.Context ?? [])
            )
            .FirstOrDefault(f => f!["name"]!.GetValue<string>() == field)
            ?["type"]?.GetValue<string>();
}

/// <summary>
/// One state of a glossary machine: <see cref="Phrase"/>, whether it is <see cref="Committed"/>, the rule its context
/// <see cref="Requires"/>, and its own <see cref="Context"/>, null when it has the machine's.
/// </summary>
internal sealed record GlossaryState(
    string Name,
    string Phrase,
    bool Committed,
    JsonObject? Requires,
    JsonArray? Context,
    IReadOnlyList<string>? ContextNames
);

internal sealed record GlossaryTrigger(
    string Name,
    string Phrase,
    JsonArray Input,
    IReadOnlyList<string> InputNames,
    JsonObject? Reduce
);

internal sealed record GlossaryGuard(string Name, string Holds, string Fails, JsonObject Rule);

internal sealed record GlossaryInvoke(
    string State,
    string Train,
    IReadOnlyDictionary<string, string> Input,
    JsonArray Output,
    IReadOnlyList<string> OutputNames,
    IReadOnlyDictionary<string, GlossaryOutcome> Outcomes,
    IReadOnlyDictionary<string, JsonObject> Examples
);

internal sealed record GlossaryOutcome(
    string Phrase,
    string Kind,
    JsonObject? When,
    JsonObject? Reduce
);

internal sealed record GlossaryLink(string Phrase, string Machine, string Field);

internal sealed record GlossaryView(
    string Name,
    IReadOnlyList<ViewRead> Reads,
    IReadOnlyList<ViewField> Fields
);
