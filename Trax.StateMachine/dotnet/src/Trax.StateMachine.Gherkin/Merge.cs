using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Sets up a context in which a guard holds or fails, for <c>Given Ada's order has nothing in it</c>. Only a guard on one
/// context field can be set up: a guard on the step's input is said in the When, and one combining rules or bound by
/// name is set up through the Whens that reach it.
/// </summary>
internal static class Satisfy
{
    internal static JsonObject? Context(
        GlossaryMachine machine,
        string guardName,
        bool holds,
        JsonObject context,
        out string? why
    )
    {
        why = null;
        var rule = IrMachine.ReadRule(machine.Guards[guardName].Rule);
        var copy = (JsonObject)context.DeepClone();
        var phrase = holds ? machine.Guards[guardName].Holds : machine.Guards[guardName].Fails;
        string? field = rule switch
        {
            Rule.Present r when r.Source == RuleSource.Context => r.Field,
            Rule.Absent r when r.Source == RuleSource.Context => r.Field,
            Rule.NonEmpty r when r.Source == RuleSource.Context => r.Field,
            Rule.Compare r when r.Source == RuleSource.Context => r.Field,
            Rule.BoolEquals r when r.Source == RuleSource.Context => r.Field,
            Rule.Count r when r.Source == RuleSource.Context => r.Field,
            Rule.Length r when r.Source == RuleSource.Context => r.Field,
            Rule.OneOf r when r.Source == RuleSource.Context => r.Field,
            _ => null,
        };
        if (field is null)
        {
            why = rule is Rule.All or Rule.Any or Rule.Custom
                ? $"'{phrase}' cannot be set up by a Given: its guard combines rules. Reach it through the Whens that make it so."
                : $"'{phrase}' reads the step's input, not the context, so it is said in the When, not a Given.";
            return null;
        }
        var type = machine
            .Context.FirstOrDefault(f => f!["name"]!.GetValue<string>() == field)
            ?["type"]?.GetValue<string>();
        if (type is null)
        {
            why =
                $"'{phrase}' reads '{field}', which is not one of the {machine.Reference}'s context fields.";
            return null;
        }

        JsonNode? value = (rule, holds) switch
        {
            (Rule.Present, true) or (Rule.Absent, false) or (Rule.NonEmpty, true) => Sample(type),
            (Rule.Present, false) or (Rule.Absent, true) => null,
            (Rule.NonEmpty, false) => Empty(type),
            (Rule.Compare c, _) => Number(type, Side(c.Op, c.Value, holds)),
            (Rule.BoolEquals b, _) => JsonValue.Create(b.Value == holds),
            (Rule.Count c, _) => new JsonArray(
                Enumerable
                    .Range(0, (int)Math.Max(0, Side(c.Op, c.Value, holds)))
                    .Select(_ => (JsonNode?)Sample("string"))
                    .ToArray()
            ),
            (Rule.Length c, _) => JsonValue.Create(
                new string('x', (int)Math.Max(0, Side(c.Op, c.Value, holds)))
            ),
            (Rule.OneOf o, true) when o.Values.Count > 0 => JsonValue.Create(o.Values[0]),
            (Rule.OneOf o, _) => JsonValue.Create(string.Concat(o.Values) + "-other"),
            _ => null,
        };
        copy[field] = value;
        if (RuleEvaluator.Evaluate(rule, copy, null) != holds)
        {
            why = $"No value of '{field}' makes '{phrase}' {(holds ? "hold" : "fail")}.";
            return null;
        }
        return copy;
    }

    // A number on the side of the comparison the guard should land on.
    private static double Side(CompareOp op, double value, bool holds) =>
        (op, holds) switch
        {
            (CompareOp.GreaterThan, true) or (CompareOp.LessOrEqual, false) => Math.Floor(value)
                + 1,
            (CompareOp.GreaterOrEqual, true) or (CompareOp.LessThan, false) => Math.Ceiling(value),
            (CompareOp.LessThan, true) or (CompareOp.GreaterOrEqual, false) => Math.Ceiling(value)
                - 1,
            (CompareOp.LessOrEqual, true) or (CompareOp.GreaterThan, false) => Math.Floor(value),
            (CompareOp.EqualTo, true) => value,
            _ => value + 1,
        };

    private static JsonNode Number(string type, double value) =>
        type == "integer" ? JsonValue.Create((long)value) : JsonValue.Create(value);

    private static JsonNode? Sample(string type) =>
        type switch
        {
            "string" => JsonValue.Create("x"),
            "integer" or "number" => JsonValue.Create(1),
            "boolean" => JsonValue.Create(true),
            "array" => new JsonArray(JsonValue.Create("x")),
            _ => new JsonObject { ["x"] = "x" },
        };

    private static JsonNode? Empty(string type) =>
        type switch
        {
            "string" => JsonValue.Create(""),
            "array" => new JsonArray(),
            "object" => new JsonObject(),
            _ => null,
        };
}

/// <summary>
/// Merges every scenario's observations of one machine into its IR. Per state and trigger: one target makes an edge; a
/// move when a guard holds and a refusal when it fails make a guarded edge, with the refusal's message; disagreement is
/// refused, naming both scenarios; silence is no edge.
/// </summary>
internal static class Merge
{
    internal static JsonObject? Machine(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        List<Problem> problems
    )
    {
        var before = problems.Count;
        var guards = machine
            .Guards.Values.Select(g => (g.Name, Rule: IrMachine.ReadRule(g.Rule)))
            .ToList();
        var edges = new List<(string From, string Trigger, string To, JsonObject Json)>();

        var observations = walks
            .SelectMany(w => w.Observations)
            .Where(o => o.Machine == machine.Id && o.Result is not Result.Denied);
        foreach (var pair in observations.GroupBy(o => (o.From, o.Trigger)))
        {
            var moves = pair.Where(o => o.Result is Result.Moved).ToList();
            var refusals = pair.Where(o => o.Result is Result.Refused).ToList();
            var targets = moves.Select(o => ((Result.Moved)o.Result).To).Distinct().ToList();
            var messages = refusals
                .Select(o => ((Result.Refused)o.Result).Message)
                .OfType<string>()
                .Distinct()
                .ToList();
            Problem At(Observation o, string message) => new(o.Scenario.File, o.Line, message);

            if (targets.Count > 1)
            {
                var a = moves.First(o => ((Result.Moved)o.Result).To == targets[0]);
                var b = moves.First(o => ((Result.Moved)o.Result).To == targets[1]);
                problems.Add(
                    At(
                        b,
                        $"{pair.Key.Trigger} from {pair.Key.From} goes to {targets[1]} here and to {targets[0]} in '{a.Scenario.Name}' ({a.Scenario.Id}). One trigger from one state goes to one state."
                    )
                );
                continue;
            }
            if (messages.Count > 1)
            {
                problems.Add(
                    At(
                        refusals[0],
                        $"{pair.Key.Trigger} from {pair.Key.From} is refused with two messages, \"{messages[0]}\" and \"{messages[1]}\". Say one."
                    )
                );
                continue;
            }
            if (targets.Count == 0)
            {
                // Refused everywhere: there is no edge, and the engine refuses the trigger. A message needs a guard to
                // be the message of.
                if (messages.Count > 0)
                    problems.Add(
                        At(
                            refusals.First(o => ((Result.Refused)o.Result).Message is not null),
                            $"\"{messages[0]}\" is the message of a refusal, and no scenario shows {pair.Key.Trigger} from {pair.Key.From} working, so there is no guard to give it. Add a scenario where it works."
                        )
                    );
                continue;
            }

            var edge = new JsonObject
            {
                ["from"] = pair.Key.From,
                ["trigger"] = pair.Key.Trigger,
                ["to"] = targets[0],
            };
            if (refusals.Count > 0)
            {
                // The guard that holds on every move and fails on every refusal; exactly one, or the features do not say
                // which condition decides.
                var deciding = guards
                    .Where(g =>
                        moves.All(o => Holds(g.Rule, o)) && refusals.All(o => !Holds(g.Rule, o))
                    )
                    .ToList();
                if (deciding.Count != 1)
                {
                    var refused = refusals[0];
                    var move = moves[0];
                    problems.Add(
                        At(
                            refused,
                            deciding.Count == 0
                                ? $"{pair.Key.Trigger} from {pair.Key.From} is refused here and works in '{move.Scenario.Name}' ({move.Scenario.Id}), and no guard in the glossary tells the two apart."
                                : $"{pair.Key.Trigger} from {pair.Key.From} is refused here and works in '{move.Scenario.Name}' ({move.Scenario.Id}), and the guards {string.Join(", ", deciding.Select(g => g.Name))} all tell them apart. Add a scenario that separates them."
                        )
                    );
                    continue;
                }
                edge["guard"] = machine.Guards[deciding[0].Name].Rule.DeepClone();
                if (messages.Count == 1)
                    edge["guardMessage"] = messages[0];
            }
            if (machine.Triggers[pair.Key.Trigger].Reduce is { } reduce)
                edge["reduce"] = reduce.DeepClone();
            edges.Add((pair.Key.From, pair.Key.Trigger, targets[0], edge));
        }

        var outcomes = Outcomes(machine, walks, problems);
        var timers = Timers(machine, walks, problems);
        if (problems.Count > before)
            return null;

        var fields = new JsonArray(machine.Context.Select(f => f!.DeepClone()).ToArray());
        var context = new JsonObject();
        foreach (var state in machine.States.Keys.Order(StringComparer.Ordinal))
            context[state] = new JsonObject { ["fields"] = fields.DeepClone() };
        var inputs = new JsonObject();
        // A trigger's input is declared on its edges, so a trigger no scenario fires has no input to declare.
        var fired = edges.Select(e => e.Trigger).ToHashSet(StringComparer.Ordinal);
        foreach (
            var trigger in machine
                .Triggers.Values.Where(t => t.InputNames.Count > 0 && fired.Contains(t.Name))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
        )
            inputs[trigger.Name] = new JsonObject { ["fields"] = trigger.Input.DeepClone() };
        var transitions = new JsonArray();
        foreach (
            var edge in edges
                .OrderBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.Trigger, StringComparer.Ordinal)
                .ThenBy(e => e.To, StringComparer.Ordinal)
        )
            transitions.Add(edge.Json);

        // A machine serves the triggers its scenarios fire and do not deny. One no scenario fires has no edge and no
        // rule, and one only ever denied is one no one may fire, so neither is served (a client sending it is refused).
        var served = walks
            .SelectMany(w => w.Observations)
            .Where(o => o.Machine == machine.Id && o.Result is not Result.Denied)
            .Select(o => o.Trigger)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var ir = new JsonObject
        {
            ["id"] = machine.Id,
            ["version"] = 1,
            ["initialState"] = machine.Initial,
            ["initialContext"] = Walk.InitialContext(machine),
            ["states"] = Sorted(machine.States.Keys),
            ["triggers"] = Sorted(served),
            ["committedStates"] = new JsonArray(),
            ["context"] = context,
            ["inputs"] = inputs,
            ["transitions"] = transitions,
        };
        if (outcomes.Count > 0)
            ir["outcomes"] = outcomes;
        if (timers.Count > 0)
            ir["timers"] = timers;
        return ir;
    }

    private static bool Holds(Rule rule, Observation o) =>
        RuleEvaluator.Evaluate(rule, o.Context, o.Input);

    // Each invoking state's three outcome triggers, their edges in the glossary's order of outcomes. Every kind needs
    // somewhere to go, or the machine cannot be built; a scenario says where.
    private static JsonObject Outcomes(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        List<Problem> problems
    )
    {
        var outcomes = new JsonObject();
        var observed = walks
            .SelectMany(w => w.Outcomes)
            .Where(o => o.Machine == machine.Id)
            .ToList();
        foreach (var invoke in machine.Invokes.Values.OrderBy(i => i.State, StringComparer.Ordinal))
        foreach (var kind in new[] { "done", "failed", "cancelled" })
        {
            var edges = new JsonArray();
            foreach (var outcome in invoke.Outcomes.Values.Where(o => o.Kind == kind))
            {
                var targets = observed
                    .Where(o => o.State == invoke.State && o.Phrase == outcome.Phrase)
                    .Select(o => o.To)
                    .Distinct()
                    .ToList();
                if (targets.Count > 1)
                {
                    var o = observed.First(x => x.Phrase == outcome.Phrase && x.To == targets[1]);
                    problems.Add(
                        new Problem(
                            o.Scenario.File,
                            o.Line,
                            $"'{outcome.Phrase}' goes to {targets[1]} here and to {targets[0]} elsewhere. One outcome goes to one state."
                        )
                    );
                    continue;
                }
                if (targets.Count == 0)
                    continue;
                var edge = new JsonObject { ["to"] = targets[0] };
                if (outcome.When is { } when)
                    edge["guard"] = when.DeepClone();
                if (outcome.Reduce is { } reduce)
                    edge["reduce"] = reduce.DeepClone();
                edges.Add(edge);
            }
            if (edges.Count == 0)
            {
                problems.Add(
                    new Problem(
                        "glossary",
                        0,
                        $"{machine.Id}: {invoke.State} invokes {invoke.Train}, and no scenario says where a {kind} run takes it. Every invoking state routes done, failed and cancelled."
                    )
                );
                continue;
            }
            var entry = new JsonObject
            {
                ["state"] = invoke.State,
                ["outcome"] = kind,
                ["edges"] = edges,
            };
            if (kind == "done")
                entry["output"] = new JsonObject { ["fields"] = invoke.Output.DeepClone() };
            outcomes[$"{invoke.State}.{kind}"] = entry;
        }
        return outcomes;
    }

    // Each state's timer, from the instances that left it once time passed: where they went, and the shortest wait that
    // moved one, which every stay in the state must be shorter than. Its rules are filled in once access is derived.
    private static JsonArray Timers(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        List<Problem> problems
    )
    {
        var timers = new JsonArray();
        var moved = walks.SelectMany(w => w.Timers).Where(o => o.Machine == machine.Id).ToList();
        var stays = walks.SelectMany(w => w.Stays).Where(o => o.Machine == machine.Id).ToList();
        foreach (var state in moved.Select(o => o.From).Distinct().Order(StringComparer.Ordinal))
        {
            var fired = moved.Where(o => o.From == state).ToList();
            var targets = fired.Select(o => o.To).Distinct().ToList();
            if (targets.Count > 1)
            {
                var other = fired.First(o => o.To == targets[1]);
                problems.Add(
                    new Problem(
                        other.Scenario.File,
                        other.Line,
                        $"Time takes {machine.Id} from {state} to {targets[1]} here and to {targets[0]} elsewhere. A state's timer goes to one state."
                    )
                );
                continue;
            }
            var shortest = fired.MinBy(o => o.Elapsed)!;
            if (shortest.Elapsed <= TimeSpan.Zero)
            {
                problems.Add(
                    new Problem(
                        shortest.Scenario.File,
                        shortest.Line,
                        $"{machine.Id} leaves {state} with no time passing there. Say how long: 'When 5 minutes pass'."
                    )
                );
                continue;
            }
            foreach (
                var stay in stays.Where(s => s.State == state && s.Elapsed >= shortest.Elapsed)
            )
                problems.Add(
                    new Problem(
                        stay.Scenario.File,
                        stay.Line,
                        $"{machine.Id} is still {state} after {Said(stay.Elapsed)} here, and leaves it after {Said(shortest.Elapsed)} in '{shortest.Scenario.Name}' ({shortest.Scenario.Id})."
                    )
                );
            if (targets[0] == state)
                problems.Add(
                    new Problem(
                        shortest.Scenario.File,
                        shortest.Line,
                        $"Time takes {machine.Id} from {state} back into {state}, and a timer never enters its own state."
                    )
                );
            if (machine.SystemKey is null && machine.Invokes.ContainsKey(targets[0]))
                problems.Add(
                    new Problem(
                        shortest.Scenario.File,
                        shortest.Line,
                        $"Time takes {machine.Id} into {targets[0]}, which runs a train, and a user's machine never starts a train on a timer: the host refuses it, since no user is there to run it as."
                    )
                );
            timers.Add(
                new JsonObject
                {
                    ["state"] = state,
                    ["trigger"] = $"{state}.after",
                    ["target"] = targets[0],
                    ["rules"] = new JsonArray(),
                    ["after"] = IsoDuration.Format(shortest.Elapsed),
                }
            );
        }
        return timers;
    }

    private static string Said(TimeSpan time) => IsoDuration.Format(time);

    private static JsonArray Sorted(IEnumerable<string> names) =>
        new(names.Order(StringComparer.Ordinal).Select(n => (JsonNode?)n).ToArray());
}

/// <summary>
/// Replays every scenario on the IRs derivation wrote, with the engine, and refuses the derivation if any step answers
/// otherwise than the scenario says. It is the check that derivation never writes a machine its own features fail.
/// </summary>
internal static class SelfCheck
{
    internal static IEnumerable<Problem> Replay(
        Walk walk,
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        JsonArray policies
    )
    {
        var engines = new Dictionary<string, IrMachine>(StringComparer.Ordinal);
        var problems = new List<Problem>();
        IrMachine Engine(string machine)
        {
            if (!engines.TryGetValue(machine, out var engine))
                engines[machine] = engine = new IrMachine(irs[machine]);
            return engine;
        }
        var snapshots = new Dictionary<InstanceKey, Snapshot>();
        // When each instance entered its state, on the scenario's clock: a timer counts from there.
        var entered = new Dictionary<InstanceKey, TimeSpan>();
        var clock = TimeSpan.Zero;
        Snapshot Of(InstanceKey key, string? state = null)
        {
            if (!snapshots.TryGetValue(key, out var snapshot))
            {
                var engine = Engine(key.Machine);
                snapshot = engine.CreateInitialSnapshot();
                entered[key] = clock;
            }
            if (state is not null)
            {
                snapshot = snapshot with { State = state };
                entered[key] = clock;
            }
            return snapshots[key] = snapshot;
        }
        // A transition enters its target, a self-transition included, so the timer starts again.
        void Enter(InstanceKey key, Snapshot snapshot, TimeSpan at)
        {
            snapshots[key] = snapshot;
            entered[key] = at;
        }
        // The policies an instance entering a state sets off, fired as the server fires them: a broadcast on every instance
        // waiting in its state, a system policy on the system instance, a linked one on the same owner's instances.
        void React(InstanceKey source, string entered, int depth)
        {
            if (depth > 8)
                return;
            foreach (var policy in policies)
            {
                if (
                    policy!["when"]!["machine"]!.GetValue<string>() != source.Machine
                    || policy["when"]!["enters"]!.GetValue<string>() != entered
                )
                    continue;
                var then = policy["then"]!;
                var target = then["machine"]!.GetValue<string>();
                var trigger = then["trigger"]!.GetValue<string>();
                var reached = policy["shape"]!.GetValue<string>() switch
                {
                    "broadcast" => snapshots.Where(kv =>
                        kv.Key.Machine == target
                        && kv.Value.State == then["inState"]!.GetValue<string>()
                    ),
                    "toSystem" => snapshots.Where(kv =>
                        kv.Key.Machine == target && kv.Key.Owner is null
                    ),
                    _ => snapshots.Where(kv =>
                        kv.Key.Machine == target && kv.Key.Owner == source.Owner
                    ),
                };
                foreach (var (key, snapshot) in reached.ToList())
                    if (
                        Engine(target).Advance(snapshot, trigger)
                        is AdvanceResult.Transitioned fired
                    )
                    {
                        Enter(key, fired.Snapshot, clock);
                        React(key, fired.Snapshot.State, depth + 1);
                    }
            }
        }
        TimeSpan? TimerOf(InstanceKey key, string state) =>
            irs[key.Machine]["timers"] is JsonArray timers
            && timers.FirstOrDefault(t => t!["state"]!.GetValue<string>() == state) is { } timer
            && IsoDuration.TryParse(timer["after"]!.GetValue<string>(), out var after)
                ? after
                : null;
        void Fail(long line, string message) =>
            problems.Add(
                new Problem(
                    walk.Scenario.File,
                    line,
                    $"The derived machine answers this differently: {message} (scenario '{walk.Scenario.Name}', {walk.Scenario.Id})"
                )
            );

        (InstanceKey Key, AdvanceResult Result)? last = null;
        foreach (var (step, meaning) in walk.Steps)
            switch (step.Kind, meaning)
            {
                case (StepKind.Given, Meaning.Start s):
                    Of(new InstanceKey(s.Machine, s.Person));
                    break;
                case (StepKind.Given, Meaning.InState s):
                    Of(new InstanceKey(s.Machine, s.Owner), s.State);
                    break;
                case (StepKind.Given, Meaning.Guard g):
                    var guarded = Of(new InstanceKey(g.Machine, g.Owner));
                    if (
                        Satisfy.Context(
                            glossary.Machines[g.Machine],
                            g.Name,
                            g.Holds,
                            guarded.Context,
                            out _
                        ) is
                        { } context
                    )
                        snapshots[new InstanceKey(g.Machine, g.Owner)] = guarded with
                        {
                            Context = context,
                        };
                    break;
                case (StepKind.When, Meaning.Act or Meaning.Happens):
                    var (machine, owner, trigger, input) = meaning switch
                    {
                        Meaning.Act a => (a.Machine, a.Owner, a.Trigger, a.Input),
                        Meaning.Happens h => (h.Machine, h.Owner, h.Trigger, h.Input),
                        _ => throw new InvalidOperationException(),
                    };
                    var key = new InstanceKey(machine, owner);
                    var result = Engine(machine).Advance(Of(key), trigger, input);
                    if (result is AdvanceResult.Transitioned moved)
                    {
                        Enter(key, moved.Snapshot, clock);
                        React(key, moved.Snapshot.State, 0);
                    }
                    last = (key, result);
                    break;
                case (StepKind.When, Meaning.Outcome o):
                    var waiting = snapshots.First(kv =>
                        kv.Key.Machine == o.Machine && kv.Value.State == o.State
                    );
                    var invoke = glossary.Machines[o.Machine].Invokes[o.State];
                    var kind = invoke.Outcomes[o.Phrase].Kind;
                    var outcome = Engine(o.Machine)
                        .ApplyOutcome(
                            waiting.Value,
                            $"{o.State}.{kind}",
                            invoke.Examples.GetValueOrDefault(o.Phrase)?.DeepClone()
                        );
                    if (outcome is AdvanceResult.Transitioned applied)
                    {
                        Enter(waiting.Key, applied.Snapshot, clock);
                        React(waiting.Key, applied.Snapshot.State, 0);
                    }
                    last = (waiting.Key, outcome);
                    break;
                case (StepKind.When, Meaning.Passes p):
                    // Every timer due by the new time fires, earliest first, and a timer its target starts fires too
                    // when it is due in turn.
                    clock += p.Time;
                    last = null;
                    for (var fired = 0; fired < 1000; fired++)
                    {
                        var due = snapshots
                            .Select(kv =>
                                (
                                    kv.Key,
                                    kv.Value,
                                    At: TimerOf(kv.Key, kv.Value.State) is { } after
                                        ? entered[kv.Key] + after
                                        : (TimeSpan?)null
                                )
                            )
                            .Where(d => d.At is { } at && at <= clock)
                            .OrderBy(d => d.At)
                            .ThenBy(d => d.Key.Machine, StringComparer.Ordinal)
                            .ThenBy(d => d.Key.Owner, StringComparer.Ordinal)
                            .FirstOrDefault();
                        if (due.At is not { } fireAt)
                            break;
                        if (
                            Engine(due.Key.Machine)
                                .ApplyTimer(due.Value, $"{due.Value.State}.after")
                            is AdvanceResult.Transitioned timed
                        )
                        {
                            Enter(due.Key, timed.Snapshot, fireAt);
                            React(due.Key, timed.Snapshot.State, 0);
                        }
                        else
                            break;
                    }
                    break;
                case (StepKind.Then, Meaning.InState s):
                    var at = new InstanceKey(s.Machine, s.Owner);
                    if (
                        last is { } l
                        && l.Key == at
                        && l.Result is AdvanceResult.Rejected r
                        && !s.Still
                    )
                        Fail(
                            step.Line,
                            $"'{step.Text}', and the machine refuses it ({r.Reason}: {r.Detail})."
                        );
                    else if (Of(at).State != s.State)
                        Fail(
                            step.Line,
                            $"'{step.Text}', and the machine has it in {Of(at).State}."
                        );
                    break;
                case (StepKind.Then, Meaning.Told t):
                    if (
                        last?.Result
                            is not AdvanceResult.Rejected
                            {
                                Reason: RefusalCode.GuardFailed
                            } refused
                        || refused.Detail != t.Message
                    )
                        Fail(
                            step.Line,
                            $"'{step.Text}', and the machine answers {Describe(last?.Result)}."
                        );
                    break;
            }
        return problems;
    }

    private static string Describe(AdvanceResult? result) =>
        result switch
        {
            AdvanceResult.Transitioned t => $"by moving to {t.Snapshot.State}",
            AdvanceResult.Rejected r => $"{r.Reason} \"{r.Detail}\"",
            _ => "nothing",
        };
}
