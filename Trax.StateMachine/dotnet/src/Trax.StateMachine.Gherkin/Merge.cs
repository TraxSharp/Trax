using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

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
            .Where(o =>
                string.Equals(o.Machine, machine.Id, StringComparison.Ordinal)
                && o.Result is not Result.Denied
            );
        foreach (var pair in observations.GroupBy(o => (o.From, o.Trigger)))
        {
            var moves = pair.Where(o => o.Result is Result.Moved).ToList();
            var refusals = pair.Where(o => o.Result is Result.Refused).ToList();
            var targets = moves
                .Select(o => ((Result.Moved)o.Result).To)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var messages = refusals
                .Select(o => ((Result.Refused)o.Result).Message)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Problem At(Observation o, string message) => new(o.Scenario.File, o.Line, message);

            if (targets.Count > 1)
            {
                var a = moves.First(o =>
                    string.Equals(((Result.Moved)o.Result).To, targets[0], StringComparison.Ordinal)
                );
                var b = moves.First(o =>
                    string.Equals(((Result.Moved)o.Result).To, targets[1], StringComparison.Ordinal)
                );
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
            if (machine.Triggers[pair.Key.Trigger].Guard is { } named)
            {
                // The glossary names the guard: it must be what the scenarios show.
                var rule = guards
                    .Single(g => string.Equals(g.Name, named, StringComparison.Ordinal))
                    .Rule;
                var broken =
                    moves.FirstOrDefault(o => !Holds(machine, rule, o))
                    ?? refusals.FirstOrDefault(o => Holds(machine, rule, o));
                if (broken is not null)
                {
                    problems.Add(
                        At(
                            broken,
                            $"{pair.Key.Trigger} from {pair.Key.From} is decided by the guard {named}, and it "
                                + $"{(broken.Result is Result.Moved ? "fails where the trigger works" : "holds where the trigger is refused")} here."
                        )
                    );
                    continue;
                }
                edge["guard"] = machine.Guards[named].Rule.DeepClone();
                if (messages.Count == 1)
                    edge["guardMessage"] = messages[0];
            }
            else if (refusals.Count > 0)
            {
                // The guard that holds on every move and fails on every refusal; exactly one, or the features do not say
                // which condition decides.
                var deciding = guards
                    .Where(g =>
                        moves.TrueForAll(o => Holds(machine, g.Rule, o))
                        && refusals.TrueForAll(o => !Holds(machine, g.Rule, o))
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
            if (machine.Triggers[pair.Key.Trigger].Effect is { } effect)
                edge["effect"] = new JsonObject
                {
                    ["keyPrefix"] = effect.KeyPrefix ?? $"{machine.Id}:{pair.Key.Trigger}",
                };
            edges.Add((pair.Key.From, pair.Key.Trigger, targets[0], edge));
        }

        var outcomes = Outcomes(machine, walks, problems);
        var timers = Timers(machine, walks, problems);
        ReservedTargets(
            machine,
            walks,
            edges.Select(e => (e.From, e.Trigger, e.To)),
            outcomes,
            timers,
            problems
        );
        if (problems.Count > before)
            return null;

        var context = new JsonObject();
        foreach (var state in machine.States.Keys.Order(StringComparer.Ordinal))
            context[state] = new JsonObject { ["fields"] = machine.ContextOf(state).DeepClone() };
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
            .Where(o =>
                string.Equals(o.Machine, machine.Id, StringComparison.Ordinal)
                && o.Result is not Result.Denied
            )
            .Select(o => o.Trigger)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var ir = new JsonObject
        {
            ["id"] = machine.Id,
            ["version"] = machine.Version,
            ["initialState"] = machine.Initial,
            ["initialContext"] = Walk.InitialContext(machine),
            ["states"] = Sorted(machine.States.Keys),
            ["triggers"] = Sorted(served),
            ["committedStates"] = Sorted(
                machine.Shapes.Values.Where(s => s.Committed).Select(s => s.Name)
            ),
            ["context"] = context,
            ["inputs"] = inputs,
            ["transitions"] = transitions,
        };
        var invariants = new JsonObject();
        foreach (
            var shape in machine
                .Shapes.Values.Where(s => s.Requires is not null)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
        )
            invariants[shape.Name] = shape.Requires!.DeepClone();
        if (invariants.Count > 0)
            ir["invariants"] = invariants;
        if (outcomes.Count > 0)
            ir["outcomes"] = outcomes;
        if (timers.Count > 0)
            ir["timers"] = timers;
        if (machine.Differential)
        {
            var differential = Differential(machine, walks, served, outcomes);
            if (differential.Count == 0)
            {
                problems.Add(
                    new Problem(
                        Problem.Glossary,
                        0,
                        $"{machine.Id} asks for a differential block, and no scenario sends it an input, gives an "
                            + "outcome's output or sets an instance up in a state, so there is nothing to write."
                    )
                    {
                        Pointer = $"/machines/{machine.Id}/differential",
                    }
                );
                return null;
            }
            ir["differential"] = differential;
        }
        return ir;
    }

    // The differential block, from what the scenarios did: every input a trigger the machine serves was sent with, every
    // output an outcome it routes was given, and every context a Given put an instance in before a When fired from it,
    // so the enumerator starts from the states the scenarios set up as well as the initial one. Each list is distinct
    // and in canonical order, so the block, and the IR's hash, move only when a scenario changes what it sends.
    private static JsonObject Differential(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        IReadOnlyCollection<string> served,
        JsonObject outcomes
    )
    {
        var samples = new SortedDictionary<string, SortedDictionary<string, JsonNode>>(
            StringComparer.Ordinal
        );
        void Add(
            SortedDictionary<string, SortedDictionary<string, JsonNode>> into,
            string key,
            JsonNode value
        )
        {
            if (!into.TryGetValue(key, out var list))
                into[key] = list = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
            list.TryAdd(CanonicalJson.Serialize(value), value);
        }

        var observations = walks
            .SelectMany(w => w.Observations)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        foreach (
            var o in observations.Where(o =>
                o.Input is not null && served.Contains(o.Trigger, StringComparer.Ordinal)
            )
        )
            Add(samples, o.Trigger, o.Input!);
        foreach (
            var o in walks
                .SelectMany(w => w.Outcomes)
                .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
        )
        {
            var invoke = machine.Invokes[o.State];
            if (
                string.Equals(invoke.Outcomes[o.Phrase].Kind, "done", StringComparison.Ordinal)
                && outcomes.ContainsKey($"{o.State}.done")
                && invoke.Examples.TryGetValue(o.Phrase, out var example)
            )
                Add(samples, $"{o.State}.done", example);
        }

        var initial = CanonicalJson.Serialize(Walk.InitialContext(machine));
        var seeds = new SortedDictionary<string, SortedDictionary<string, JsonNode>>(
            StringComparer.Ordinal
        );
        foreach (var o in observations.Where(o => o.Arranged))
            if (
                !(
                    string.Equals(o.From, machine.Initial, StringComparison.Ordinal)
                    && string.Equals(
                        CanonicalJson.Serialize(o.Context),
                        initial,
                        StringComparison.Ordinal
                    )
                )
            )
                Add(seeds, o.From, o.Context);

        var block = new JsonObject();
        if (samples.Count > 0)
        {
            var written = new JsonObject();
            foreach (var (key, list) in samples)
                written[key] = new JsonArray(list.Values.Select(v => v.DeepClone()).ToArray());
            block["samples"] = written;
        }
        if (seeds.Count > 0)
        {
            // One seed is written as the context itself and several as a list, as the exporter writes them.
            var written = new JsonObject();
            foreach (var (state, list) in seeds)
                written[state] =
                    list.Count == 1
                        ? list.Values.First().DeepClone()
                        : new JsonArray(list.Values.Select(v => v.DeepClone()).ToArray());
            block["seeds"] = written;
        }
        return block;
    }

    private static bool Holds(GlossaryMachine machine, Rule rule, Observation o) =>
        RuleEvaluator.Evaluate(rule, o.Context, o.Input, machine.ExampleRules);

    // Each invoking state's three outcome triggers, their edges in the glossary's order of outcomes. Every kind needs
    // somewhere to go, or the machine cannot be built; a scenario says where.
    // The host's rule for an invoked train's outcome targets (Fluent.RefuseOtherEdgesIntoOutcomeTargets), said first
    // here: a target means "the train produced this", so the machine may not start in one, and no ordinary move may
    // enter one, unless the target invokes a train itself. A self-loop does not enter it. A timer may not enter one
    // either (Fluent.BuildTimers).
    private static void ReservedTargets(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        IEnumerable<(string From, string Trigger, string To)> edges,
        JsonObject outcomes,
        JsonArray timers,
        List<Problem> problems
    )
    {
        var observed = walks
            .SelectMany(w => w.Outcomes)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        // Two outcomes of one run going to the same state reserve it once.
        var reserved = outcomes
            .SelectMany(o =>
                o.Value!["edges"]!
                    .AsArray()
                    .Select(e =>
                        (
                            State: o.Value["state"]!.GetValue<string>(),
                            Target: e!["to"]!.GetValue<string>()
                        )
                    )
            )
            .Where(r => !machine.Invokes.ContainsKey(r.Target))
            .Distinct()
            .ToList();
        foreach (var (state, target) in reserved)
        {
            var at = observed.FirstOrDefault(o =>
                string.Equals(o.State, state, StringComparison.Ordinal)
                && string.Equals(o.To, target, StringComparison.Ordinal)
            );
            if (string.Equals(target, machine.Initial, StringComparison.Ordinal))
                problems.Add(
                    new Problem(
                        at?.Scenario.File ?? Problem.Glossary,
                        at?.Line ?? 0,
                        $"{machine.Id} starts at {target}, and an outcome of the train {state} invokes goes there. An "
                            + "outcome's state is reserved for what the train produced, so no instance could be started. "
                            + "Send the outcome to a state of its own, with a move from there back if the flow returns."
                    )
                    {
                        Pointer = $"/machines/{machine.Id}/invokes/{state}",
                    }
                );
            foreach (
                var into in edges.Where(e =>
                    string.Equals(e.To, target, StringComparison.Ordinal)
                    && !string.Equals(e.From, target, StringComparison.Ordinal)
                )
            )
            {
                var by = walks
                    .SelectMany(w => w.Observations)
                    .FirstOrDefault(o =>
                        string.Equals(o.Machine, machine.Id, StringComparison.Ordinal)
                        && string.Equals(o.From, into.From, StringComparison.Ordinal)
                        && string.Equals(o.Trigger, into.Trigger, StringComparison.Ordinal)
                        && o.Result is Result.Moved
                    );
                problems.Add(
                    new Problem(
                        by?.Scenario.File ?? Problem.Glossary,
                        by?.Line ?? 0,
                        $"{into.Trigger} moves {machine.Id} from {into.From} to {target}, where an outcome of the "
                            + $"train {state} invokes goes. An outcome's state is reserved for what the train "
                            + "produced: give the move a state of its own."
                    )
                    {
                        Pointer = $"/machines/{machine.Id}/invokes/{state}",
                    }
                );
            }
            foreach (
                var timer in timers.Where(t =>
                    string.Equals(
                        t!["target"]!.GetValue<string>(),
                        target,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                var from = timer!["state"]!.GetValue<string>();
                var by = walks
                    .SelectMany(w => w.Timers)
                    .FirstOrDefault(o =>
                        string.Equals(o.Machine, machine.Id, StringComparison.Ordinal)
                        && string.Equals(o.From, from, StringComparison.Ordinal)
                        && string.Equals(o.To, target, StringComparison.Ordinal)
                    );
                problems.Add(
                    new Problem(
                        by?.Scenario.File ?? Problem.Glossary,
                        by?.Line ?? 0,
                        $"Time takes {machine.Id} from {from} to {target}, where an outcome of the train {state} "
                            + "invokes goes. An outcome's state is reserved for what the train produced: send the "
                            + "timer to a state of its own."
                    )
                    {
                        Pointer = $"/machines/{machine.Id}/invokes/{state}",
                    }
                );
            }
        }
    }

    private static JsonObject Outcomes(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        List<Problem> problems
    )
    {
        var outcomes = new JsonObject();
        var observed = walks
            .SelectMany(w => w.Outcomes)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        foreach (var invoke in machine.Invokes.Values.OrderBy(i => i.State, StringComparer.Ordinal))
        foreach (var kind in new[] { "done", "failed", "cancelled" })
        {
            var edges = new JsonArray();
            foreach (
                var outcome in invoke.Outcomes.Values.Where(o =>
                    string.Equals(o.Kind, kind, StringComparison.Ordinal)
                )
            )
            {
                var targets = observed
                    .Where(o =>
                        string.Equals(o.State, invoke.State, StringComparison.Ordinal)
                        && string.Equals(o.Phrase, outcome.Phrase, StringComparison.Ordinal)
                    )
                    .Select(o => o.To)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (targets.Count > 1)
                {
                    var o = observed.First(x =>
                        string.Equals(x.Phrase, outcome.Phrase, StringComparison.Ordinal)
                        && string.Equals(x.To, targets[1], StringComparison.Ordinal)
                    );
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
                // The host's rule (InvokesStartupValidator), said first here: on a user's machine an outcome that enters
                // another state that runs a train would queue that run with no user there to run it as.
                if (!machine.IsSystem && machine.Invokes.ContainsKey(targets[0]))
                {
                    var chained = observed.First(o =>
                        string.Equals(o.State, invoke.State, StringComparison.Ordinal)
                        && string.Equals(o.Phrase, outcome.Phrase, StringComparison.Ordinal)
                        && string.Equals(o.To, targets[0], StringComparison.Ordinal)
                    );
                    problems.Add(
                        new Problem(
                            chained.Scenario.File,
                            chained.Line,
                            $"'{outcome.Phrase}' takes {machine.Id} from {invoke.State} into {targets[0]}, which runs a "
                                + "train, and a user's machine never starts a train on another train's outcome: the host "
                                + "refuses it, since no user is there to run it as. Send the outcome to a state of its "
                                + $"own and let a person's trigger enter {targets[0]}, or have one train do both."
                        )
                    );
                }
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
                        Problem.Glossary,
                        0,
                        $"{machine.Id}: {invoke.State} invokes {invoke.Train}, and no scenario says where a {kind} run takes it. Every invoking state routes done, failed and cancelled."
                    )
                    {
                        Pointer = $"/machines/{machine.Id}/invokes/{invoke.State}",
                    }
                );
                continue;
            }
            var entry = new JsonObject
            {
                ["state"] = invoke.State,
                ["outcome"] = kind,
                ["edges"] = edges,
            };
            if (string.Equals(kind, "done", StringComparison.Ordinal))
                entry["output"] = new JsonObject { ["fields"] = ByName(invoke.Output) };
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
        var shown = new Dictionary<string, TimerObservation>(StringComparer.Ordinal);
        var moved = walks
            .SelectMany(w => w.Timers)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        var stays = walks
            .SelectMany(w => w.Stays)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        foreach (
            var state in moved
                .Select(o => o.From)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        )
        {
            var fired = moved
                .Where(o => string.Equals(o.From, state, StringComparison.Ordinal))
                .ToList();
            var targets = fired.Select(o => o.To).Distinct(StringComparer.Ordinal).ToList();
            if (targets.Count > 1)
            {
                var other = fired.First(o =>
                    string.Equals(o.To, targets[1], StringComparison.Ordinal)
                );
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
            // The host's builder refuses a timer longer than a timer may wait: say so here, as it says it.
            if (shortest.Elapsed > MachineTimers.MaxDuration)
                problems.Add(
                    new Problem(
                        shortest.Scenario.File,
                        shortest.Line,
                        $"{machine.Id}: {MachineTimers.TooLong(state, shortest.Elapsed)}"
                    )
                );
            foreach (
                var stay in stays.Where(s =>
                    string.Equals(s.State, state, StringComparison.Ordinal)
                    && s.Elapsed >= shortest.Elapsed
                )
            )
                problems.Add(
                    new Problem(
                        stay.Scenario.File,
                        stay.Line,
                        $"{machine.Id} is still {state} after {Said(stay.Elapsed)} here, and leaves it after {Said(shortest.Elapsed)} in '{shortest.Scenario.Name}' ({shortest.Scenario.Id})."
                    )
                );
            if (string.Equals(targets[0], state, StringComparison.Ordinal))
                problems.Add(
                    new Problem(
                        shortest.Scenario.File,
                        shortest.Line,
                        $"Time takes {machine.Id} from {state} back into {state}, and a timer never enters its own state."
                    )
                );
            if (!machine.IsSystem && machine.Invokes.ContainsKey(targets[0]))
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
            shown[state] = shortest;
        }

        // A loop of timers alone the host refuses to build (EFF-219): say so here, at the scenarios that showed them.
        var next = shown.ToDictionary(s => s.Key, s => s.Value.To, StringComparer.Ordinal);
        foreach (var loop in TimerLoops.Find(next, machine.States.Keys.ToList()))
        foreach (var state in loop.Distinct(StringComparer.Ordinal))
            problems.Add(
                new Problem(
                    shown[state].Scenario.File,
                    shown[state].Line,
                    $"{machine.Id}: {TimerLoops.Describe(loop)}"
                )
            );
        return timers;
    }

    private static string Said(TimeSpan time) => IsoDuration.Format(time);

    // A train's output fields in the order the host reflects them from its output type (SchemaReflection: by name,
    // ordinal, at every depth), so the IR the host exports from the train is the IR written here, whatever order the
    // glossary lists them in.
    private static JsonArray ByName(JsonArray fields) =>
        new(
            fields
                .Select(f => (JsonObject)f!.DeepClone())
                .OrderBy(f => f["name"]!.GetValue<string>(), StringComparer.Ordinal)
                .Select(f => (JsonNode?)Nested(f))
                .ToArray()
        );

    // A record field's own fields, and a list's or a dictionary's value shape, sorted the same way.
    private static JsonObject Nested(JsonObject shape)
    {
        if (shape["fields"] is JsonArray fields)
            shape["fields"] = ByName(fields);
        if (shape["values"] is JsonObject values)
            Nested(values);
        return shape;
    }

    private static JsonArray Sorted(IEnumerable<string> names) =>
        new(names.Order(StringComparer.Ordinal).Select(n => (JsonNode?)n).ToArray());
}
