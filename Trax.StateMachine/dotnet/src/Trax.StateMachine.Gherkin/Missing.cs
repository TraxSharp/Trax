using System.Globalization;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

internal enum GapKind
{
    /// <summary>A machine no scenario uses, so none is derived.</summary>
    Machine,

    /// <summary>A trigger no scenario fires, so it is not served.</summary>
    Trigger,

    /// <summary>A state no scenario reaches.</summary>
    State,

    /// <summary>A guard no scenario uses.</summary>
    Guard,

    /// <summary>A guarded move no scenario shows refused.</summary>
    Refusal,

    /// <summary>An invoked train's outcome no scenario shows.</summary>
    Outcome,

    /// <summary>A view no scenario reads, so it is not served.</summary>
    View,

    /// <summary>An actor a rule admits by its roles that no scenario shows: a grant nobody meant.</summary>
    Grant,
}

/// <summary>
/// Something the glossary names that no scenario shows, and, when one can be written, a scenario to paste that shows it.
/// A stub is offered only if the features with it pasted derive with no problem, and what they derive differs only by
/// what the stub claims (ADR <c>Trax.Docs/adr/0058</c>). A stub guesses where the move goes: read it before pasting it.
/// </summary>
internal sealed record Gap(GapKind Kind, string Machine, string What, string Message, string? Stub);

internal static class Missing
{
    private const string Person = "Ada";

    /// <summary>
    /// The gaps of features that derive. Features that do not derive have their problems to fix first: an empty list
    /// and the problems.
    /// </summary>
    internal static (IReadOnlyList<Gap> Gaps, IReadOnlyList<Problem> Problems) Find(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features
    )
    {
        var baseline = FeatureDerivation.Derive(glossary, features);
        if (baseline.Problems.Count > 0)
            return ([], baseline.Problems);
        var host = JsonNode.Parse(baseline.Host!)!.AsObject();
        var provenance = host["provenance"] as JsonObject ?? [];
        bool Shown(string machine, string part) => provenance[machine]?[part] is not null;

        var (scenarios, _) = FeatureFiles.Parse(features);
        var resolver = new StepResolver(glossary);
        var walks = scenarios.Select(s => Walk.Run(s, glossary, resolver)).ToList();
        var guardSteps = walks
            .SelectMany(w => w.Steps)
            .Select(s => s.Meaning)
            .OfType<Meaning.Guard>()
            .ToList();
        var guardsUsed = guardSteps.Select(g => (g.Machine, g.Name)).ToHashSet();
        var heldFor = HeldFor(walks);

        var gaps = new List<Gap>();
        void Add(
            GapKind kind,
            string machine,
            string what,
            string message,
            IEnumerable<Claim> stubs
        )
        {
            string? stub = null;
            foreach (var claim in stubs)
                if (Holds(glossary, features, baseline, claim))
                {
                    stub = claim.Scenario;
                    break;
                }
            gaps.Add(new Gap(kind, machine, what, message, stub));
        }

        foreach (var machine in glossary.Machines.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var m = machine.Id;
            if (!baseline.Irs.ContainsKey(m))
            {
                Add(
                    GapKind.Machine,
                    m,
                    m,
                    $"{m}: no scenario uses it, so it is not derived.",
                    MachineStubs(glossary, machine)
                );
                continue;
            }
            var ir = JsonNode.Parse(baseline.Irs[m])!.AsObject();
            var edges = ir["transitions"]!
                .AsArray()
                .Select(e =>
                    (
                        From: e!["from"]!.GetValue<string>(),
                        Trigger: e["trigger"]!.GetValue<string>(),
                        To: e["to"]!.GetValue<string>(),
                        Guard: e["guard"]
                    )
                )
                .ToList();
            // A guard decides a move when the derived edge carries its rule: the scenarios showed it holding on the
            // move and failing on the refusal, whether a Given set it up or the When's value did.
            bool Decides(GlossaryGuard guard) =>
                edges.Any(e => e.Guard is not null && JsonNode.DeepEquals(e.Guard, guard.Rule));
            var reached = edges
                .Select(e => e.To)
                .Append(machine.Initial)
                .Concat(OutcomeTargets(ir))
                // A timer moves an instance as surely as a trigger does.
                .Concat(
                    (ir["timers"] as JsonArray ?? [])
                        .Select(t => t?["target"]?.GetValue<string>())
                        .OfType<string>()
                )
                .ToHashSet(StringComparer.Ordinal);

            foreach (
                var trigger in machine.Triggers.Values.OrderBy(t => t.Name, StringComparer.Ordinal)
            )
                if (!Shown(m, $"command {trigger.Name}"))
                    Add(
                        GapKind.Trigger,
                        m,
                        trigger.Name,
                        $"{m} {trigger.Name}: no scenario fires it, so it is not served.",
                        reached
                            .Order(StringComparer.Ordinal)
                            .SelectMany(from =>
                                Targets(machine, from)
                                    .OrderBy(to => reached.Contains(to))
                                    .Select(to =>
                                        Move(
                                            glossary,
                                            machine,
                                            from,
                                            trigger,
                                            to,
                                            $"command {trigger.Name}"
                                        )
                                    )
                            )
                            .OfType<Claim>()
                    );

            foreach (var (state, _) in machine.States.OrderBy(s => s.Key, StringComparer.Ordinal))
                if (!reached.Contains(state))
                    Add(
                        GapKind.State,
                        m,
                        state,
                        $"{m} {state}: no scenario moves an instance there.",
                        reached
                            .Order(StringComparer.Ordinal)
                            .SelectMany(from =>
                                machine
                                    .Triggers.Values.OrderBy(t => Shown(m, $"command {t.Name}"))
                                    .ThenBy(t => t.Name, StringComparer.Ordinal)
                                    .Select(trigger =>
                                        Move(
                                            glossary,
                                            machine,
                                            from,
                                            trigger,
                                            state,
                                            $"edge {from} {trigger.Name} {state}"
                                        )
                                            is { } move
                                            ? move with
                                            {
                                                Trigger = trigger.Name,
                                            }
                                            : null
                                    )
                            )
                            .OfType<Claim>()
                    );

            foreach (
                var guard in machine.Guards.Values.OrderBy(g => g.Name, StringComparer.Ordinal)
            )
                if (!guardsUsed.Contains((m, guard.Name)) && !Decides(guard))
                    Add(
                        GapKind.Guard,
                        m,
                        guard.Name,
                        guard.OnInput
                            ? $"{m} guard {guard.Name}: no scenario shows it refusing a When's value."
                            : $"{m} guard {guard.Name}: no scenario sets it up.",
                        edges
                            .Select(e =>
                                Guarded(
                                    glossary,
                                    machine,
                                    guard,
                                    e.From,
                                    machine.Triggers[e.Trigger],
                                    e.To
                                )
                            )
                            .OfType<Claim>()
                    );

            // A move no scenario shows refused: a guarded edge, or one a guard is shown holding for and never failing.
            foreach (var edge in edges.DistinctBy(e => (e.From, e.Trigger)))
            {
                var guards = edge.Guard is not null
                    ? machine.Guards.Values.ToList()
                    : machine
                        .Guards.Values.Where(g =>
                            heldFor.Contains((m, edge.Trigger, g.Name))
                            && !guardSteps.Any(x => x.Machine == m && x.Name == g.Name && !x.Holds)
                        )
                        .ToList();
                if (!Shown(m, $"guard {edge.From} {edge.Trigger}") && guards.Count > 0)
                    Add(
                        GapKind.Refusal,
                        m,
                        $"{edge.From} {edge.Trigger}",
                        $"{m} {edge.Trigger} from {edge.From}: no scenario shows it refused.",
                        guards
                            .Select(g =>
                                Refused(
                                    glossary,
                                    machine,
                                    g,
                                    edge.From,
                                    machine.Triggers[edge.Trigger]
                                )
                            )
                            .OfType<Claim>()
                    );
            }

            foreach (
                var invoke in machine.Invokes.Values.OrderBy(i => i.State, StringComparer.Ordinal)
            )
            foreach (
                var (phrase, outcome) in invoke.Outcomes.OrderBy(o => o.Key, StringComparer.Ordinal)
            )
                if (!Shown(m, $"outcome {invoke.State} {outcome.Kind}"))
                    Add(
                        GapKind.Outcome,
                        m,
                        $"{invoke.State} {outcome.Kind}",
                        $"{m} {invoke.State}: no scenario shows '{phrase}'.",
                        Targets(machine, invoke.State)
                            .Select(to =>
                                OutcomeStub(
                                    glossary,
                                    machine,
                                    invoke.State,
                                    phrase,
                                    outcome.Kind,
                                    to
                                )
                            )
                    );
        }

        foreach (var view in glossary.Views.Values.OrderBy(v => v.Name, StringComparer.Ordinal))
            if (host["views"]?[view.Name] is null)
                Add(
                    GapKind.View,
                    view.Reads[0].Machine,
                    view.Name,
                    $"view {view.Name}: no scenario reads it, so it is not served.",
                    [ViewStub(glossary, view, baseline)]
                );

        foreach (var line in baseline.Access!.Split('\n').Where(l => l.Contains("(no scenario)")))
            gaps.Add(
                new Gap(
                    GapKind.Grant,
                    line[..line.IndexOf(' ')],
                    line[..line.IndexOf(':')],
                    $"{line}: an actor marked (no scenario) is admitted by its roles alone. Give it roles of its own, or "
                        + "show it in a scenario.",
                    null
                )
            );
        return (gaps, []);
    }

    // What a stub says it shows, and the parts of the derivation it may change.
    private sealed record Claim(
        string Scenario,
        string Machine,
        string Part,
        string? Trigger = null,
        string? View = null,
        bool NewMachine = false
    );

    // The features with the stub pasted derive with no problem, show the claimed part, and change nothing else: no other
    // machine's IR, no part of the host file outside the claim, and no line of the access table but the claim's.
    private static bool Holds(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features,
        Derived baseline,
        Claim claim
    )
    {
        var stubbed = FeatureDerivation.Derive(
            glossary,
            [.. features, ("(stub).feature", "Feature: Stub\n" + claim.Scenario)]
        );
        if (stubbed.Problems.Count > 0)
            return false;
        var host = JsonNode.Parse(stubbed.Host!)!.AsObject();
        if (host["provenance"]?[claim.Machine]?[claim.Part] is null)
            return false;

        foreach (var (id, ir) in baseline.Irs)
            if (id != claim.Machine && stubbed.Irs.GetValueOrDefault(id) != ir)
                return false;
        if (stubbed.Irs.Keys.Any(id => id != claim.Machine && !baseline.Irs.ContainsKey(id)))
            return false;
        if (!claim.NewMachine && !baseline.Irs.ContainsKey(claim.Machine))
            return false;

        if (Outside(JsonNode.Parse(baseline.Host!)!.AsObject(), claim) != Outside(host, claim))
            return false;

        var before = baseline.Access!.Split('\n').ToHashSet(StringComparer.Ordinal);
        var after = stubbed.Access!.Split('\n').ToHashSet(StringComparer.Ordinal);
        return before
            .Except(after)
            .Concat(after.Except(before))
            .All(line =>
                claim switch
                {
                    { NewMachine: true } => line.StartsWith(
                        $"{claim.Machine} ",
                        StringComparison.Ordinal
                    ),
                    { View: { } view } => line.StartsWith(
                        $"view {view}:",
                        StringComparison.Ordinal
                    ),
                    { Trigger: { } trigger } => line.StartsWith(
                        $"{claim.Machine} {trigger}:",
                        StringComparison.Ordinal
                    ),
                    _ => false,
                }
            );
    }

    // The host file without what the claim may change: provenance, the claimed machine's hash (its IR changes), the
    // claimed trigger's or view's entry, and, for a new machine, the machine.
    private static string Outside(JsonObject host, Claim claim)
    {
        var copy = host.DeepClone().AsObject();
        copy.Remove("provenance");
        if (copy["machines"]![claim.Machine] is JsonObject machine)
        {
            if (claim.NewMachine)
                copy["machines"]!.AsObject().Remove(claim.Machine);
            else
            {
                machine.Remove("schemaHash");
                if (claim.Trigger is { } trigger && machine["triggers"] is JsonObject triggers)
                {
                    triggers.Remove(trigger);
                    if (triggers.Count == 0)
                        machine.Remove("triggers");
                }
            }
        }
        if (claim.View is { } view && copy["views"] is JsonObject views)
        {
            views.Remove(view);
            if (views.Count == 0)
                copy.Remove("views");
        }
        return copy.ToJsonString();
    }

    // Each trigger fired after a Given said a guard holds on the same machine: the move the guard was set up for.
    private static HashSet<(string Machine, string Trigger, string Guard)> HeldFor(
        IEnumerable<Walk> walks
    )
    {
        var held = new HashSet<(string, string, string)>();
        foreach (var walk in walks)
        {
            var holding = new List<(string Machine, string Guard)>();
            foreach (var (_, meaning) in walk.Steps)
                switch (meaning)
                {
                    case Meaning.Guard { Holds: true } g:
                        holding.Add((g.Machine, g.Name));
                        break;
                    case Meaning.Act or Meaning.Happens:
                        var (machine, trigger) = meaning is Meaning.Act a
                            ? (a.Machine, a.Trigger)
                            : (
                                ((Meaning.Happens)meaning).Machine,
                                ((Meaning.Happens)meaning).Trigger
                            );
                        foreach (var h in holding.Where(h => h.Machine == machine))
                            held.Add((machine, trigger, h.Guard));
                        holding.Clear();
                        break;
                }
        }
        return held;
    }

    // Where a stub tries a move to: every other state first, then the same one.
    private static IEnumerable<string> Targets(GlossaryMachine machine, string from) =>
        machine.States.Keys.Where(s => s != from).Order(StringComparer.Ordinal).Append(from);

    // A user's machine is set up by its owner having one; a system machine, which the host starts, by a move of its own.
    private static IEnumerable<Claim> MachineStubs(Glossary glossary, GlossaryMachine machine)
    {
        if (!machine.IsSystem)
        {
            var lines = Arrange(glossary, machine, machine.Initial);
            lines.Add($"Then {Instance(machine)} {machine.States[machine.Initial]}");
            yield return new Claim(
                Scenario($"{Instance(machine)} is set up", lines),
                machine.Id,
                "start",
                NewMachine: true
            );
            yield break;
        }
        foreach (
            var trigger in machine.Triggers.Values.OrderBy(t => t.Name, StringComparer.Ordinal)
        )
        foreach (var to in Targets(machine, machine.Initial))
            if (
                Move(glossary, machine, machine.Initial, trigger, to, $"command {trigger.Name}") is
                { } move
            )
                yield return move with
                {
                    NewMachine = true,
                };
    }

    private static Claim? Move(
        Glossary glossary,
        GlossaryMachine machine,
        string from,
        GlossaryTrigger trigger,
        string to,
        string part
    )
    {
        if (Fire(machine, trigger) is not { } when)
            return null;
        var lines = Arrange(glossary, machine, from);
        lines.Add($"When {when}");
        lines.Add(
            $"Then {Instance(machine)} {(to == from ? StillPhrase(machine.States[to]) : machine.States[to])}"
        );
        return new Claim(
            Scenario($"{trigger.Name} from {from}", lines),
            machine.Id,
            part,
            Trigger: part.StartsWith("command ", StringComparison.Ordinal) ? trigger.Name : null
        );
    }

    private static Claim? Guarded(
        Glossary glossary,
        GlossaryMachine machine,
        GlossaryGuard guard,
        string from,
        GlossaryTrigger trigger,
        string to
    )
    {
        // A guard on the input is shown by a When whose value it refuses, beside the move already shown.
        if (guard.OnInput)
            return Refused(glossary, machine, guard, from, trigger);
        if (Fire(machine, trigger) is not { } when)
            return null;
        var lines = Arrange(glossary, machine, from);
        lines.Add($"And {Instance(machine)} {guard.Holds}");
        lines.Add($"When {when}");
        lines.Add($"Then {Instance(machine)} {machine.States[to]}");
        return new Claim(
            Scenario($"{trigger.Name} when {guard.Holds}", lines),
            machine.Id,
            $"edge {from} {trigger.Name} {to}"
        );
    }

    private static Claim? Refused(
        Glossary glossary,
        GlossaryMachine machine,
        GlossaryGuard guard,
        string from,
        GlossaryTrigger trigger
    )
    {
        var lines = Arrange(glossary, machine, from);
        string name;
        if (guard.OnInput)
        {
            if (Failing(machine, trigger, guard) is not { } values)
                return null;
            if (Fire(machine, trigger, values) is not { } refused)
                return null;
            lines.Add($"When {refused}");
            name = $"{trigger.Name} refused by {guard.Name}";
        }
        else
        {
            if (Fire(machine, trigger) is not { } when)
                return null;
            lines.Add($"And {Instance(machine)} {guard.Fails}");
            lines.Add($"When {when}");
            name = $"{trigger.Name} when {guard.Fails}";
        }
        lines.Add($"Then {Instance(machine)} {StillPhrase(machine.States[from])}");
        return new Claim(Scenario(name, lines), machine.Id, $"guard {from} {trigger.Name}");
    }

    // The trigger's input values a guard on the input fails for: the stub's own values with one field changed, the
    // first change that makes the guard fail. Null when no such change does, or the guard is not the trigger's to fail.
    private static Dictionary<string, string>? Failing(
        GlossaryMachine machine,
        GlossaryTrigger trigger,
        GlossaryGuard guard
    )
    {
        var rule = IrMachine.ReadRule(guard.Rule);
        var types = trigger
            .Input.Select(f =>
                (Name: f!["name"]!.GetValue<string>(), Type: f["type"]!.GetValue<string>())
            )
            .ToList();
        var values = types.ToDictionary(f => f.Name, f => Sample(f.Type), StringComparer.Ordinal);
        if (values.Values.Any(v => v is null))
            return null;
        foreach (var (name, type) in types)
        foreach (var candidate in Candidates(type))
        {
            var tried = new Dictionary<string, string>(values!, StringComparer.Ordinal)
            {
                [name] = candidate,
            };
            var input = new JsonObject();
            foreach (var (field, fieldType) in types)
                input[field] = Typed(fieldType, tried[field]);
            if (!RuleEvaluator.Evaluate(rule, new JsonObject(), input, machine.ExampleRules))
                return tried;
        }
        return null;
    }

    // The value a stub fires a trigger with, as the feature says it.
    private static string? Sample(string type) =>
        type switch
        {
            "integer" or "number" => "1",
            "string" => "x",
            "boolean" => "true",
            _ => null,
        };

    private static IEnumerable<string> Candidates(string type) =>
        type switch
        {
            "integer" or "number" => ["0", "-1", "1000000"],
            "string" => [""],
            "boolean" => ["false"],
            _ => [],
        };

    private static JsonNode? Typed(string type, string value) =>
        type switch
        {
            "integer" => JsonValue.Create(long.Parse(value, CultureInfo.InvariantCulture)),
            "number" => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
            "boolean" => JsonValue.Create(value == "true"),
            _ => JsonValue.Create(value),
        };

    private static Claim OutcomeStub(
        Glossary glossary,
        GlossaryMachine machine,
        string state,
        string phrase,
        string kind,
        string to
    )
    {
        var lines = Arrange(glossary, machine, state);
        lines.Add($"When {phrase}");
        lines.Add($"Then {Instance(machine)} {machine.States[to]}");
        return new Claim(Scenario(phrase, lines), machine.Id, $"outcome {state} {kind}");
    }

    private static Claim ViewStub(Glossary glossary, GlossaryView view, Derived baseline)
    {
        var read = glossary.Machines[view.Reads[0].Machine];
        var field = view.Fields.First(f => f.Machine == read.Id);
        var lines = Arrange(glossary, read, read.Initial);
        if (read.IsSystem)
            lines.Insert(0, $"Given {Person} is {glossary.Actors.Values.First().Noun}");
        var initial = Engine.Initial(baseline, read);
        var shown = ViewValues.Shown(
            view,
            field.Name,
            Person,
            [(new InstanceKey(read.Id, read.IsSystem ? null : Person), read.Initial, initial)]
        );
        lines.Add(
            $"Then {Person}'s {view.Name} {(view.Name.EndsWith('s') ? "show" : "shows")} {field.Name} \"{shown.FirstOrDefault()}\""
        );
        return new Claim(
            Scenario($"{Person} reads the {view.Name}", lines),
            read.Id,
            $"view {view.Name}",
            View: view.Name
        );
    }

    // The Givens that put the stub's instance in a state: a person who owns it and has one, or the system's.
    private static List<string> Arrange(Glossary glossary, GlossaryMachine machine, string state)
    {
        var lines = new List<string>();
        if (!machine.IsSystem)
        {
            lines.Add($"Given {Person} is {glossary.Actors[machine.OwnedBy![0]].Noun}");
            lines.Add($"And {Person} {machine.Start}");
            if (state != machine.Initial)
                lines.Add($"And {Instance(machine)} {machine.States[state]}");
        }
        else
            lines.Add($"Given {Instance(machine)} {machine.States[state]}");
        return lines;
    }

    // The When of a trigger: the person firing it, or the server when its phrase starts with the instance.
    private static string? Fire(
        GlossaryMachine machine,
        GlossaryTrigger trigger,
        IReadOnlyDictionary<string, string>? values = null
    )
    {
        var text = trigger.Phrase;
        foreach (var field in trigger.Input)
        {
            var name = field!["name"]!.GetValue<string>();
            var value =
                values?.GetValueOrDefault(name) ?? Sample(field["type"]!.GetValue<string>());
            if (value is null)
                return null;
            text = text.Replace("{" + name + "}", $"\"{value}\"", StringComparison.Ordinal);
        }
        var server = text.StartsWith("{subject}", StringComparison.Ordinal) || machine.IsSystem;
        text = text.Replace(
            "{subject}",
            server ? Instance(machine) : $"their {machine.Reference}",
            StringComparison.Ordinal
        );
        return server ? text : $"{Person} {text}";
    }

    // A user's instance by its owner, the system's by its name, and a keyed one by its name and a sample key.
    private static string Instance(GlossaryMachine machine) =>
        machine.KeyedBy is { } parts
            ? $"{machine.Reference} {string.Join(" ", parts.Select(p => $"\"{p}-1\""))}"
        : machine.IsSystem ? machine.Reference
        : $"{Person}'s {machine.Reference}";

    // "is a draft" as a stay: "is still a draft".
    private static string StillPhrase(string phrase)
    {
        var space = phrase.IndexOf(' ');
        return space < 0 ? $"{phrase} still" : $"{phrase[..space]} still{phrase[space..]}";
    }

    private static string Scenario(string name, IEnumerable<string> steps) =>
        $"  Scenario: {name}\n" + string.Concat(steps.Select(s => $"    {s}\n"));

    // The states an invoked train's outcomes move an instance to. The IR keys outcomes by "<state>.<kind>".
    private static IEnumerable<string> OutcomeTargets(JsonObject ir) =>
        ir["outcomes"] is JsonObject outcomes
            ? outcomes.SelectMany(o =>
                o.Value!["edges"]!.AsArray().Select(e => e!["to"]!.GetValue<string>())
            )
            : [];

    private static class Engine
    {
        internal static JsonObject Initial(Derived baseline, GlossaryMachine machine) =>
            baseline.Irs.TryGetValue(machine.Id, out var ir)
                ? JsonNode.Parse(ir)!["initialContext"]?.AsObject().DeepClone().AsObject() ?? []
                : [];
    }
}
