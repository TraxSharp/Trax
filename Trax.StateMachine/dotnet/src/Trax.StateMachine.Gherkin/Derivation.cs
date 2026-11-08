using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// What derivation wrote: each machine's IR (canonical JSON, by id) and the host file, or the problems that stopped it.
/// A derivation with problems writes nothing. <see cref="UiSteps"/> are the Thens no phrase resolves, which are about
/// the UI and are left to the browser runner.
/// </summary>
internal sealed record Derived(
    IReadOnlyDictionary<string, string> Irs,
    string? Host,
    string? Access,
    IReadOnlyList<Problem> Problems,
    IReadOnlyList<Problem> UiSteps
);

/// <summary>
/// Derives machines from features and a glossary (ADR <c>Trax.Docs/adr/0058</c>): resolve each step, walk each
/// scenario into observations, merge them per state and trigger into edges, write the IRs and the host file, and replay
/// every scenario on what was written. A derivation that its own features fail is a problem, never a machine.
/// </summary>
internal static class FeatureDerivation
{
    internal static Derived Derive(
        Glossary glossary,
        IReadOnlyList<(string Path, string Text)> features
    )
    {
        var (scenarios, problems) = FeatureFiles.Parse(features);
        var resolver = new StepResolver(glossary);
        var walks = scenarios.Select(s => Walk.Run(s, glossary, resolver)).ToList();
        foreach (var walk in walks)
            problems.AddRange(walk.Problems);
        var uiSteps = walks.SelectMany(w => w.UiSteps).ToList();

        var irs = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        // A machine no scenario uses is not derived, as a view no scenario reads is not served: its IR would serve
        // states nothing shows.
        var used = Used(walks);
        if (problems.Count == 0)
            foreach (var machine in glossary.Machines.Values.Where(m => used.Contains(m.Id)))
            {
                var ir = Merge.Machine(machine, walks, problems);
                if (ir is not null)
                    irs[machine.Id] = ir;
            }

        var policies =
            problems.Count == 0 ? PolicyMerge.Derive(glossary, walks, irs, problems) : [];

        var access = problems.Count == 0 ? Access.Derive(glossary, walks, irs, problems) : null;
        if (problems.Count > 0 || access is null)
            return new Derived(
                new Dictionary<string, string>(),
                null,
                null,
                Sorted(problems),
                Sorted(uiSteps)
            );

        foreach (var (id, ir) in irs)
            TimerRules.Fill(ir, access.Entries[id]);
        var written = irs.ToDictionary(
            kv => kv.Key,
            kv => CanonicalJson.Serialize(kv.Value),
            StringComparer.Ordinal
        );
        var host = Host(glossary, walks, written, access, policies);

        // Every scenario is run on what was derived, access included, as the host would answer it. A derivation its
        // own features fail is a problem, never a machine.
        var hostFile = HostFile.Read(host);
        foreach (var walk in walks)
        {
            ScenarioResult run;
            try
            {
                run = ScenarioRunner
                    .Run(walk, glossary, new MemoryDriver(irs, hostFile, glossary))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A replay that cannot run is a problem with this scenario, not a crash of the whole derivation.
                problems.Add(
                    new Problem(
                        walk.Scenario.File,
                        walk.Scenario.Line,
                        $"The derived machine could not be run on this scenario: {e.Message} (scenario '{walk.Scenario.Name}', {walk.Scenario.Id})"
                    )
                );
                continue;
            }
            if (run.Failure is { } failure)
                problems.Add(
                    new Problem(
                        walk.Scenario.File,
                        failure.Step.Line,
                        $"The derived machine answers this differently: {failure.Message} (scenario '{walk.Scenario.Name}', {walk.Scenario.Id})"
                    )
                );
        }
        if (problems.Count > 0)
            return new Derived(
                new Dictionary<string, string>(),
                null,
                null,
                Sorted(problems),
                Sorted(uiSteps)
            );

        return new Derived(written, host, access.Table, [], Sorted(uiSteps));
    }

    // The host file: its machines, then its policies, views and provenance when it has any.
    private static JsonObject Document(
        JsonObject machines,
        JsonArray policies,
        JsonObject views,
        JsonObject provenance
    )
    {
        var document = new JsonObject { ["version"] = HostFile.Version, ["machines"] = machines };
        if (policies.Count > 0)
            document["policies"] = policies.DeepClone();
        if (views.Count > 0)
            document["views"] = views.DeepClone();
        if (provenance.Count > 0)
            document["provenance"] = provenance;
        return document;
    }

    private static HashSet<string> Used(IEnumerable<Walk> walks)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var walk in walks)
        {
            used.UnionWith(walk.Observations.Select(o => o.Machine));
            used.UnionWith(walk.Starts.Select(s => s.Machine));
            used.UnionWith(walk.Policies.SelectMany(p => new[] { p.When, p.Target }));
            foreach (var (_, meaning) in walk.Steps)
                if (
                    meaning switch
                    {
                        Meaning.Start m => m.Machine,
                        Meaning.Act m => m.Machine,
                        Meaning.Happens m => m.Machine,
                        Meaning.InState m => m.Machine,
                        Meaning.Guard m => m.Machine,
                        Meaning.Outcome m => m.Machine,
                        _ => null,
                    } is
                    { } machine
                )
                    used.Add(machine);
        }
        return used;
    }

    private static List<Problem> Sorted(IEnumerable<Problem> problems) =>
        problems
            .Distinct()
            .OrderBy(p => p.File, StringComparer.Ordinal)
            .ThenBy(p => p.Line)
            .ThenBy(p => p.Message, StringComparer.Ordinal)
            .ToList();

    // The server-only half: each machine's owner, the IR it was derived with, and the train each invoking state runs.
    // Who may start, read and fire each machine is the access derivation's.
    private static string Host(
        Glossary glossary,
        IReadOnlyList<Walk> walks,
        IReadOnlyDictionary<string, string> irs,
        Access access,
        JsonArray policies
    )
    {
        var machines = new JsonObject();
        foreach (var (id, ir) in irs)
        {
            var machine = glossary.Machines[id];
            var entry = new JsonObject
            {
                ["schemaHash"] = Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes(ir))
                ),
                ["owner"] =
                    machine.SystemKey is { } key ? new JsonObject { ["system"] = key }
                    : machine.KeyedBy is { } keyedBy
                        ? new JsonObject
                        {
                            ["system"] = new JsonObject
                            {
                                ["keyedBy"] = new JsonArray(
                                    keyedBy.Select(p => (JsonNode?)p).ToArray()
                                ),
                            },
                        }
                    : "user",
            };
            foreach (var (name, value) in access.Entries[id])
                entry[name] = value!.DeepClone();
            if (machine.Invokes.Count > 0)
            {
                var invokes = new JsonObject();
                foreach (
                    var invoke in machine.Invokes.Values.OrderBy(
                        i => i.State,
                        StringComparer.Ordinal
                    )
                )
                {
                    var input = new JsonObject();
                    foreach (
                        var (field, from) in invoke.Input.OrderBy(
                            kv => kv.Key,
                            StringComparer.Ordinal
                        )
                    )
                        input[field] = from;
                    invokes[invoke.State] = new JsonObject
                    {
                        ["train"] = invoke.Train,
                        ["input"] = input,
                    };
                }
                entry["invokes"] = invokes;
            }
            machines[id] = entry;
        }
        return CanonicalJson.Serialize(
            Document(
                machines,
                policies,
                access.Views,
                Provenance.Derive(glossary, walks, irs.Keys.ToList(), access.Views)
            )
        );
    }
}

/// <summary>One instance a scenario talks about: a user machine's, by its owner, or a system machine's.</summary>
/// <summary>
/// An instance a scenario names: a user's by its owner, a system machine's at its fixed key by neither, and a keyed
/// system machine's by <see cref="Key"/>, its parts as a JSON array (<c>["T-12"]</c>).
/// </summary>
internal sealed record InstanceKey(string Machine, string? Owner, string? Key = null)
{
    /// <summary>The key's parts, or none for an instance that is not a keyed system machine's.</summary>
    internal IReadOnlyList<string> Parts =>
        Key is null
            ? []
            : JsonNode.Parse(Key)!.AsArray().Select(p => p!.GetValue<string>()).ToList();

    /// <summary>The key as a step says it: each part quoted, <c>"T-12"</c>.</summary>
    internal string Said => string.Join(" ", Parts.Select(p => $"\"{p}\""));
}

/// <summary>What a When did, as its Thens say.</summary>
internal abstract record Result
{
    internal sealed record Moved(string To) : Result;

    internal sealed record Refused(string? Message) : Result;

    internal sealed record Denied(string Person) : Result;
}

/// <summary>
/// One trigger fired in a scenario, the context and input it was fired with, and what it did. <see cref="Arranged"/> is
/// whether a Given put the instance where it was fired from, rather than a move.
/// </summary>
internal sealed record Observation(
    string Machine,
    string From,
    string Trigger,
    JsonObject Context,
    JsonObject? Input,
    string? Person,
    string? Actor,
    Result Result,
    FeatureScenario Scenario,
    long Line,
    bool Arranged = false
);

/// <summary>A person starting an instance of their own: <c>Given Ada has an order</c>.</summary>
internal sealed record StartObservation(
    string Machine,
    string Actor,
    FeatureScenario Scenario,
    long Line
);

/// <summary>An instance leaving a state once time passed: its state's timer, at most <see cref="Elapsed"/> long.</summary>
internal sealed record TimerObservation(
    string Machine,
    string From,
    TimeSpan Elapsed,
    string To,
    FeatureScenario Scenario,
    long Line
);

/// <summary>An instance still in a state after <see cref="Elapsed"/> there: its timer, if any, is longer.</summary>
internal sealed record StayObservation(
    string Machine,
    string State,
    TimeSpan Elapsed,
    FeatureScenario Scenario,
    long Line
);

/// <summary>
/// One instance moving because another did: when <see cref="When"/> (owned by <see cref="WhenOwner"/>) entered
/// <see cref="Enters"/>, <see cref="Target"/> went from <see cref="From"/> to <see cref="To"/>. A policy.
/// </summary>
internal sealed record PolicyObservation(
    string When,
    string? WhenOwner,
    string Enters,
    string Target,
    string? TargetOwner,
    string From,
    string To,
    FeatureScenario Scenario,
    long Line
);

/// <summary>One outcome of an invoked train, and where it took the instance.</summary>
internal sealed record OutcomeObservation(
    string Machine,
    string State,
    string Phrase,
    string To,
    FeatureScenario Scenario,
    long Line
);

/// <summary>
/// Walks one scenario's steps over the instances it names, recording every trigger fired and what its Thens say it did.
/// A Given sets an instance up, a When fires, and the Thens after a When settle it. An unresolved Given or When is a
/// problem; an unresolved Then is a step about the UI.
/// </summary>
internal sealed class Walk
{
    private readonly Glossary _glossary;
    private readonly FeatureScenario _scenario;
    private readonly Dictionary<string, string> _people = new(StringComparer.Ordinal);
    private readonly Dictionary<InstanceKey, Instance> _instances = [];
    private Pending? _pending;

    // A When that was refused: its Thens say nothing more, so they are not checked.
    private bool _refusedWhen;

    // The scenario's clock, which only time passing moves, and whether the last When was time passing.
    private TimeSpan _clock;
    private bool _afterTime;

    // View steps after a When, read once it is settled.
    private readonly List<(FeatureStep Step, Meaning.ViewShows View)> _views = [];
    private InstanceKey? _timed;

    private Walk(FeatureScenario scenario, Glossary glossary)
    {
        _scenario = scenario;
        _glossary = glossary;
    }

    internal FeatureScenario Scenario => _scenario;
    internal List<Problem> Problems { get; } = [];
    internal List<Problem> UiSteps { get; } = [];
    internal List<Observation> Observations { get; } = [];
    internal List<OutcomeObservation> Outcomes { get; } = [];
    internal List<StartObservation> Starts { get; } = [];
    internal List<TimerObservation> Timers { get; } = [];
    internal List<StayObservation> Stays { get; } = [];
    internal List<PolicyObservation> Policies { get; } = [];
    internal List<ViewObservation> Views { get; } = [];

    /// <summary>Each step with its meaning, for the replay; a step with none is left out.</summary>
    internal List<(FeatureStep Step, Meaning Meaning)> Steps { get; } = [];

    internal sealed class Instance(string? state, JsonObject context, TimeSpan entered)
    {
        public string? State { get; private set; } = state;
        public JsonObject Context { get; set; } = context;

        /// <summary>Whether a Given set the instance's state or context since it last moved.</summary>
        public bool Arranged { get; set; }

        /// <summary>When, on the scenario's clock, the instance entered its state: a timer counts from here.</summary>
        public TimeSpan Entered { get; private set; } = entered;

        public void Enter(string state, TimeSpan at)
        {
            State = state;
            Entered = at;
            Arranged = false;
        }
    }

    private sealed record Pending(
        Meaning When,
        InstanceKey Key,
        string From,
        JsonObject Context,
        JsonObject? Input,
        long Line,
        bool Arranged = false
    )
    {
        public List<Meaning> Thens { get; } = [];

        // Other instances the Thens say moved: what the When set off, which is a policy reacting to it.
        public List<(InstanceKey Key, string To, long Line)> Cascades { get; } = [];
    }

    internal static Walk Run(FeatureScenario scenario, Glossary glossary, StepResolver resolver)
    {
        var walk = new Walk(scenario, glossary);
        foreach (var step in scenario.Steps)
        {
            var meaning = resolver.Resolve(
                step.Text,
                walk._people,
                out var problem,
                out var unresolved
            );
            if (meaning is null)
            {
                if (unresolved && step.Kind == StepKind.Then)
                    walk.UiSteps.Add(new Problem(scenario.File, step.Line, step.Text));
                else
                    walk.Fail(step.Line, problem!);
                continue;
            }
            walk.Steps.Add((step, meaning));
            switch (step.Kind)
            {
                case StepKind.Given:
                    walk.Given(step, meaning);
                    break;
                case StepKind.When:
                    walk.Settle();
                    walk._afterTime = false;
                    var problems = walk.Problems.Count;
                    walk.When(step, meaning);
                    walk._refusedWhen = walk.Problems.Count > problems;
                    break;
                default:
                    if (!walk._refusedWhen)
                        walk.Then(step, meaning);
                    break;
            }
        }
        walk.Settle();
        return walk;
    }

    private void Fail(long line, string message) =>
        Problems.Add(
            new Problem(
                _scenario.File,
                line,
                $"{message} (scenario '{_scenario.Name}', {_scenario.Id})"
            )
        );

    private void Given(FeatureStep step, Meaning meaning)
    {
        switch (meaning)
        {
            case Meaning.Named p:
                if (_people.TryGetValue(p.Name, out var actor) && actor != p.Actor)
                    Fail(
                        step.Line,
                        $"{p.Name} is already {_glossary.Actors[actor].Noun} in this scenario."
                    );
                _people[p.Name] = p.Actor;
                break;
            case Meaning.Start s:
                var key = new InstanceKey(s.Machine, s.Person);
                if (_instances.ContainsKey(key))
                    Fail(
                        step.Line,
                        $"{s.Person} already has a {_glossary.Machines[s.Machine].Reference} in this scenario."
                    );
                _instances[key] = new Instance(
                    Initial(s.Machine),
                    InitialContext(_glossary.Machines[s.Machine]),
                    _clock
                );
                Starts.Add(
                    new StartObservation(s.Machine, _people[s.Person], _scenario, step.Line)
                );
                break;
            case Meaning.InState { Still: false } s:
                var arranged = InstanceAt(new InstanceKey(s.Machine, s.Owner, s.Key));
                arranged.Enter(s.State, _clock);
                arranged.Arranged = true;
                break;
            case Meaning.Guard g:
                var instance = InstanceAt(new InstanceKey(g.Machine, g.Owner, g.Key));
                if (
                    Satisfy.Context(
                        _glossary.Machines[g.Machine],
                        g.Name,
                        g.Holds,
                        instance.Context,
                        out var why
                    ) is
                    { } context
                )
                {
                    instance.Context = context;
                    instance.Arranged = true;
                }
                else
                    Fail(step.Line, why!);
                break;
            default:
                Fail(
                    step.Line,
                    $"'{step.Text}' is not something a Given sets up. Write it as a When or a Then."
                );
                break;
        }
    }

    private void When(FeatureStep step, Meaning meaning)
    {
        switch (meaning)
        {
            case Meaning.Act or Meaning.Happens:
                var (machine, owner, instanceKey, input) = meaning switch
                {
                    Meaning.Act a => (a.Machine, a.Owner, null, a.Input),
                    Meaning.Happens h => (h.Machine, h.Owner, h.Key, h.Input),
                    _ => throw new InvalidOperationException(),
                };
                // A person acts on their own instance, and a system machine is moved by the server alone (ADR
                // 0058).
                if (meaning is Meaning.Act act)
                {
                    var acted = _glossary.Machines[act.Machine];
                    if (acted.IsSystem)
                    {
                        Fail(
                            step.Line,
                            $"'{step.Text}': {act.Person} fires {acted.Reference}, and only the server moves a system machine. Say what happens instead: '{acted.Reference} ...'."
                        );
                        return;
                    }
                    if (act.Owner != act.Person)
                    {
                        Fail(
                            step.Line,
                            $"'{step.Text}': {act.Person} acts on {act.Owner}'s {acted.Reference}. A person acts on their own; one acting on another's needs instances shared between people, which Trax does not have yet."
                        );
                        return;
                    }
                }
                var key = new InstanceKey(machine, owner, instanceKey);
                if (!_instances.TryGetValue(key, out var instance) || instance.State is null)
                {
                    Fail(
                        step.Line,
                        $"'{step.Text}' fires on {Name(key)}, and the scenario has not said what state it is in. Give it with a Given."
                    );
                    return;
                }
                _pending = new Pending(
                    meaning,
                    key,
                    instance.State,
                    (JsonObject)instance.Context.DeepClone(),
                    input,
                    step.Line,
                    instance.Arranged
                );
                break;
            case Meaning.Outcome o:
                var waiting = _instances
                    .Where(i => i.Key.Machine == o.Machine && i.Value.State == o.State)
                    .ToList();
                if (waiting.Count != 1)
                {
                    Fail(
                        step.Line,
                        waiting.Count == 0
                            ? $"'{step.Text}' is an outcome of {o.State}, and no {_glossary.Machines[o.Machine].Reference} in this scenario is in it."
                            : $"'{step.Text}' is an outcome of {o.State}, and more than one {_glossary.Machines[o.Machine].Reference} here is in it."
                    );
                    return;
                }
                _pending = new Pending(
                    meaning,
                    waiting[0].Key,
                    o.State,
                    (JsonObject)waiting[0].Value.Context.DeepClone(),
                    null,
                    step.Line
                );
                break;
            case Meaning.Passes p:
                if (_clock > TimeSpan.MaxValue - p.Time)
                {
                    Fail(
                        step.Line,
                        $"'{step.Text}' adds up to more time than a clock holds ({TimeSpan.MaxValue.Days} days)."
                    );
                    return;
                }
                _clock += p.Time;
                _afterTime = true;
                _timed = null;
                return;
            default:
                Fail(
                    step.Line,
                    $"'{step.Text}' is not something a When does. Write it as a Given or a Then."
                );
                break;
        }
    }

    private void Then(FeatureStep step, Meaning meaning)
    {
        if (_afterTime && meaning is Meaning.InState timed)
        {
            AfterTime(step, timed);
            return;
        }
        switch (meaning)
        {
            case Meaning.InState s
                when _pending is not null
                    && _pending.Key == new InstanceKey(s.Machine, s.Owner, s.Key):
                _pending.Thens.Add(s);
                break;
            case Meaning.Told or Meaning.Denied:
                var person = meaning is Meaning.Told t
                    ? t.Person
                    : ((Meaning.Denied)meaning).Person;
                if (_pending?.When is Meaning.Act act && act.Person == person)
                    _pending.Thens.Add(meaning);
                else
                    Fail(
                        step.Line,
                        $"'{step.Text}' answers no act of {person}'s. It follows the When in which {person} acts."
                    );
                break;
            case Meaning.InState { Still: false } s
                when _pending is not null
                    && _instances.TryGetValue(
                        new InstanceKey(s.Machine, s.Owner, s.Key),
                        out var other
                    )
                    && other.State is not null
                    && other.State != s.State:
                _pending.Cascades.Add(
                    (new InstanceKey(s.Machine, s.Owner, s.Key), s.State, step.Line)
                );
                break;
            case Meaning.InState s:
                var key = new InstanceKey(s.Machine, s.Owner, s.Key);
                if (!_instances.TryGetValue(key, out var instance) || instance.State != s.State)
                    Fail(
                        step.Line,
                        $"'{step.Text}': {Name(key)} is {(instance?.State ?? "not started")} here, and nothing in this scenario moves it."
                    );
                break;
            case Meaning.ViewShows v when _pending is not null:
                // Read once the When is settled, so the view shows what the When did.
                _views.Add((step, v));
                break;
            case Meaning.ViewShows v:
                CheckView(step, v);
                break;
            case Meaning.Guard g:
                var guarded = InstanceAt(new InstanceKey(g.Machine, g.Owner, g.Key));
                var guard = _glossary.Machines[g.Machine].Guards[g.Name];
                if (
                    RuleEvaluator.Evaluate(IrMachine.ReadRule(guard.Rule), guarded.Context, null)
                    != g.Holds
                )
                    Fail(
                        step.Line,
                        $"'{step.Text}' does not hold of {Name(new InstanceKey(g.Machine, g.Owner, g.Key))} here."
                    );
                break;
            default:
                Fail(
                    step.Line,
                    $"'{step.Text}' is not something a Then checks. Write it as a Given or a When."
                );
                break;
        }
    }

    // A Then after time passed: the instance moved, which is its state's timer firing, or it is still where it was,
    // which says the timer is longer than it has been there. Only one machine moves by time in one pass; another
    // changing after it is a policy reacting to the first.
    private void AfterTime(FeatureStep step, Meaning.InState s)
    {
        var key = new InstanceKey(s.Machine, s.Owner, s.Key);
        if (!_instances.TryGetValue(key, out var instance) || instance.State is null)
        {
            Fail(
                step.Line,
                $"'{step.Text}': the scenario has not said what state {Name(key)} is in."
            );
            return;
        }
        var elapsed = _clock - instance.Entered;
        if (instance.State == s.State)
        {
            Stays.Add(new StayObservation(s.Machine, s.State, elapsed, _scenario, step.Line));
            return;
        }
        if (s.Still)
        {
            Fail(
                step.Line,
                $"'{step.Text}': {Name(key)} is {instance.State} here, so it cannot still be {s.State}."
            );
            return;
        }
        if (_timed is { } first && first.Machine != key.Machine)
        {
            // Another machine moving after the first, in one pass of time, is a policy reacting to the first.
            Policies.Add(
                new PolicyObservation(
                    first.Machine,
                    first.Owner,
                    _instances[first].State!,
                    key.Machine,
                    key.Owner,
                    instance.State,
                    s.State,
                    _scenario,
                    step.Line
                )
            );
            instance.Enter(s.State, _clock);
            return;
        }
        _timed = key;
        Timers.Add(
            new TimerObservation(s.Machine, instance.State, elapsed, s.State, _scenario, step.Line)
        );
        instance.Enter(s.State, _clock);
    }

    // What the When did, from its Thens: moved to the state they name, refused (told or still), or not allowed.
    private void Settle()
    {
        SettleWhen();
        foreach (var (step, view) in _views)
            CheckView(step, view);
        _views.Clear();
    }

    // A person reading a view: recorded for its readers, and the value checked against what their instances show.
    private void CheckView(FeatureStep step, Meaning.ViewShows v)
    {
        Views.Add(new ViewObservation(v.View, _people[v.Person], _scenario, step.Line));
        var shown = ViewValues.Shown(
            _glossary.Views[v.View],
            v.Field,
            v.Person,
            _instances.Select(i => (i.Key, i.Value.State, i.Value.Context))
        );
        if (!shown.Contains(v.Value))
            Fail(
                step.Line,
                $"'{step.Text}': {v.Person}'s {v.View} show {v.Field} {(shown.Count == 0 ? "nothing, since the scenario has no instance it reads" : string.Join(" or ", shown.Select(s => $"\"{s}\"")))}."
            );
    }

    private void SettleWhen()
    {
        if (_pending is not { } pending)
            return;
        _pending = null;
        var instance = _instances[pending.Key];
        var moved = pending
            .Thens.OfType<Meaning.InState>()
            .Where(s => !s.Still)
            .Select(s => s.State)
            .Distinct()
            .ToList();
        var still = pending.Thens.OfType<Meaning.InState>().Where(s => s.Still).ToList();
        var told = pending.Thens.OfType<Meaning.Told>().Select(t => t.Message).Distinct().ToList();
        var denied = pending.Thens.OfType<Meaning.Denied>().FirstOrDefault();

        if (still.Any(s => s.State != pending.From))
            Fail(
                pending.Line,
                $"{Name(pending.Key)} is {pending.From} when this happens, so it cannot stay {still[0].State}."
            );
        if (
            moved.Count > 1
            || (moved.Count == 1 && (still.Count > 0 || told.Count > 0 || denied is not null))
        )
        {
            Fail(
                pending.Line,
                $"The Thens after this When disagree about what it did to {Name(pending.Key)}."
            );
            return;
        }
        if (told.Count > 1)
        {
            Fail(pending.Line, "Two messages for one refusal. Say one.");
            return;
        }

        if (pending.When is Meaning.Outcome outcome)
        {
            if (moved.Count != 1)
            {
                Fail(
                    pending.Line,
                    $"Say where '{outcome.Phrase}' takes {Name(pending.Key)}: 'Then {Name(pending.Key)} is ...'."
                );
                return;
            }
            Outcomes.Add(
                new OutcomeObservation(
                    outcome.Machine,
                    outcome.State,
                    outcome.Phrase,
                    moved[0],
                    _scenario,
                    pending.Line
                )
            );
            var invoke = _glossary.Machines[outcome.Machine].Invokes[outcome.State];
            var definition = invoke.Outcomes[outcome.Phrase];
            if (definition.Reduce is { } outcomeReduce)
                instance.Context = ReductionEvaluator.Apply(
                    IrMachine.ReadReduction(outcomeReduce),
                    instance.Context,
                    invoke.Examples.GetValueOrDefault(outcome.Phrase),
                    InitialContext(_glossary.Machines[outcome.Machine])
                );
            instance.Enter(moved[0], _clock);
            Cascade(pending, moved[0]);
            return;
        }

        var (machine, trigger, person) = pending.When switch
        {
            Meaning.Act a => (a.Machine, a.Trigger, (string?)a.Person),
            Meaning.Happens h => (h.Machine, h.Trigger, null),
            _ => throw new InvalidOperationException(),
        };
        Result result;
        if (denied is not null)
            result = new Result.Denied(denied.Person);
        else if (moved.Count == 1)
            result = new Result.Moved(moved[0]);
        else if (still.Count > 0 || told.Count > 0)
            result = new Result.Refused(told.FirstOrDefault());
        else
        {
            Fail(
                pending.Line,
                $"Nothing after this When says what it did. Add 'Then {Name(pending.Key)} is ...'."
            );
            return;
        }
        Observations.Add(
            new Observation(
                machine,
                pending.From,
                trigger,
                pending.Context,
                pending.Input,
                person,
                person is null ? null : _people[person],
                result,
                _scenario,
                pending.Line,
                pending.Arranged
            )
        );

        if (result is Result.Moved m)
        {
            if (_glossary.Machines[machine].Triggers[trigger].Reduce is { } reduce)
                instance.Context = ReductionEvaluator.Apply(
                    IrMachine.ReadReduction(reduce),
                    instance.Context,
                    pending.Input,
                    InitialContext(_glossary.Machines[machine])
                );
            instance.Enter(m.To, _clock);
            Cascade(pending, m.To);
        }
        else
            Cascade(pending, null);
    }

    // What the When set off in other instances: each is a policy reacting to the When's instance entering its new
    // state. A When that moved nothing sets nothing off.
    // Each instance a When's Thens show moving too was moved by a policy. Its cause is the When's instance, unless the
    // instance is another person's and a system instance moved before it in the same cascade: then the system
    // instance entering its state moved it, as a broadcast does (a baker opening the shop bakes the waiting orders).
    private void Cascade(Pending pending, string? entered)
    {
        (InstanceKey Key, string Entered)? system =
            pending.Key.Owner is null && entered is not null ? (pending.Key, entered) : null;
        foreach (var (key, to, line) in pending.Cascades)
        {
            if (entered is null)
            {
                Fail(
                    line,
                    $"{Name(key)} moves here, and the When before it moved nothing, so nothing reacts to it."
                );
                continue;
            }
            var target = _instances[key];
            var (cause, causeEntered) =
                key.Owner is not null && key.Owner != pending.Key.Owner && system is { } moved
                    ? (moved.Key, moved.Entered)
                    : (pending.Key, entered);
            Policies.Add(
                new PolicyObservation(
                    cause.Machine,
                    cause.Owner,
                    causeEntered,
                    key.Machine,
                    key.Owner,
                    target.State!,
                    to,
                    _scenario,
                    line
                )
            );
            target.Enter(to, _clock);
            if (key.Owner is null)
                system = (key, to);
        }
    }

    // An instance a Given names before saying it has one: set up in no state yet, with the initial context.
    private Instance InstanceAt(InstanceKey key)
    {
        if (!_instances.TryGetValue(key, out var instance))
            _instances[key] = instance = new Instance(
                null,
                InitialContext(_glossary.Machines[key.Machine]),
                _clock
            );
        return instance;
    }

    private string Initial(string machine) => _glossary.Machines[machine].Initial;

    private string Name(InstanceKey key) =>
        key.Owner is { } owner ? $"{owner}'s {_glossary.Machines[key.Machine].Reference}"
        : key.Key is not null ? $"{_glossary.Machines[key.Machine].Reference} {key.Said}"
        : _glossary.Machines[key.Machine].Reference;

    /// <summary>
    /// A machine's initial context: the one the glossary gives, or each field of the initial state's context at its zero
    /// (null for a nullable one, an empty string, list or record, zero, false). The replay refuses one its own schema
    /// does not accept.
    /// </summary>
    internal static JsonObject InitialContext(GlossaryMachine machine)
    {
        if (machine.InitialContext is { } given)
            return (JsonObject)given.DeepClone();
        var context = new JsonObject();
        foreach (var field in machine.ContextOf(machine.Initial))
            context[field!["name"]!.GetValue<string>()] = field["nullable"]!.GetValue<bool>()
                ? null
                : field["type"]!.GetValue<string>() switch
                {
                    "string" => JsonValue.Create(""),
                    "integer" or "number" => JsonValue.Create(0),
                    "boolean" => JsonValue.Create(false),
                    "array" => new JsonArray(),
                    _ => new JsonObject(),
                };
        return context;
    }
}
