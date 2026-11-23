using System.Text.Json.Nodes;
using Trax.Core.Extensions;
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
            if (MergePair(machine, guards, pair, problems) is { } merged)
                edges.Add(merged);

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

        var context = Context(machine);
        var inputs = Inputs(machine, edges);
        var transitions = Transitions(edges);

        var served = Served(machine, walks);
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
        return Finish(machine, walks, served, outcomes, timers, ir, problems);
    }

    // The IR's optional blocks: invariants, outcomes, timers and the differential block, or null when the machine asks
    // for a differential block no scenario gives anything to.
    private static JsonObject? Finish(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        List<string> served,
        JsonObject outcomes,
        JsonArray timers,
        JsonObject ir,
        List<Problem> problems
    )
    {
        var invariants = new JsonObject();
        foreach (
            var shape in machine
                .Shapes.Values.Where(s => s.Requires is not null)
                .OrderBy(s => s.Name, StringComparer.Ordinal)
        )
            invariants[shape.Name] = shape.Requires.NotNull().DeepClone();
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

    // A machine serves the triggers its scenarios fire and do not deny. One no scenario fires has no edge and no
    // rule, and one only ever denied is one no one may fire, so neither is served (a client sending it is refused).
    private static List<string> Served(GlossaryMachine machine, IReadOnlyList<Walk> walks) =>
        walks
            .SelectMany(w => w.Observations)
            .Where(o =>
                string.Equals(o.Machine, machine.Id, StringComparison.Ordinal)
                && o.Result is not Result.Denied
            )
            .Select(o => o.Trigger)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static JsonObject Context(GlossaryMachine machine)
    {
        var context = new JsonObject();
        foreach (var state in machine.States.Keys.Order(StringComparer.Ordinal))
            context[state] = new JsonObject { ["fields"] = machine.ContextOf(state).DeepClone() };
        return context;
    }

    private static JsonObject Inputs(
        GlossaryMachine machine,
        List<(string From, string Trigger, string To, JsonObject Json)> edges
    )
    {
        var inputs = new JsonObject();
        // A trigger's input is declared on its edges, so a trigger no scenario fires has no input to declare.
        var fired = edges.Select(e => e.Trigger).ToHashSet(StringComparer.Ordinal);
        foreach (
            var trigger in machine
                .Triggers.Values.Where(t => t.InputNames.Count > 0 && fired.Contains(t.Name))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
        )
            inputs[trigger.Name] = new JsonObject { ["fields"] = trigger.Input.DeepClone() };
        return inputs;
    }

    private static JsonArray Transitions(
        List<(string From, string Trigger, string To, JsonObject Json)> edges
    )
    {
        var transitions = new JsonArray();
        foreach (
            var edge in edges
                .OrderBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.Trigger, StringComparer.Ordinal)
                .ThenBy(e => e.To, StringComparer.Ordinal)
        )
            transitions.Add(edge.Json);
        return transitions;
    }

    private static Problem At(Observation o, string message) =>
        new(o.Scenario.File, o.Line, message);

    // One state and trigger's observations as an edge, or null when they disagree or show no edge (a problem says why).
    private static (string From, string Trigger, string To, JsonObject Json)? MergePair(
        GlossaryMachine machine,
        List<(string Name, Rule Rule)> guards,
        IGrouping<(string From, string Trigger), Observation> pair,
        List<Problem> problems
    )
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

        if (ReportedDisagreement(pair.Key, moves, refusals, targets, messages, problems))
            return null;

        var edge = new JsonObject
        {
            ["from"] = pair.Key.From,
            ["trigger"] = pair.Key.Trigger,
            ["to"] = targets[0],
        };
        if (!Guarded(machine, guards, pair.Key, moves, refusals, messages, edge, problems))
            return null;
        if (machine.Triggers[pair.Key.Trigger].Reduce is { } reduce)
            edge["reduce"] = reduce.DeepClone();
        if (machine.Triggers[pair.Key.Trigger].Effect is { } effect)
            edge["effect"] = new JsonObject
            {
                ["keyPrefix"] = effect.KeyPrefix ?? $"{machine.Id}:{pair.Key.Trigger}",
            };
        return (pair.Key.From, pair.Key.Trigger, targets[0], edge);
    }

    // True when the observations disagree on the target or the message, or show no move at all: there is no edge.
    private static bool ReportedDisagreement(
        (string From, string Trigger) key,
        List<Observation> moves,
        List<Observation> refusals,
        List<string> targets,
        List<string> messages,
        List<Problem> problems
    )
    {
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
                    $"{key.Trigger} from {key.From} goes to {targets[1]} here and to {targets[0]} in '{a.Scenario.Name}' ({a.Scenario.Id}). One trigger from one state goes to one state."
                )
            );
            return true;
        }
        if (messages.Count > 1)
        {
            problems.Add(
                At(
                    refusals[0],
                    $"{key.Trigger} from {key.From} is refused with two messages, \"{messages[0]}\" and \"{messages[1]}\". Say one."
                )
            );
            return true;
        }
        if (targets.Count == 0)
        {
            // Refused everywhere: there is no edge, and the engine refuses the trigger. A message needs a guard to
            // be the message of.
            if (messages.Count > 0)
                problems.Add(
                    At(
                        refusals.First(o => ((Result.Refused)o.Result).Message is not null),
                        $"\"{messages[0]}\" is the message of a refusal, and no scenario shows {key.Trigger} from {key.From} working, so there is no guard to give it. Add a scenario where it works."
                    )
                );
            return true;
        }
        return false;
    }

    // Puts the guard that decides the edge on it; false when the named guard is not what the scenarios show, or no
    // single guard tells the moves from the refusals.
    private static bool Guarded(
        GlossaryMachine machine,
        List<(string Name, Rule Rule)> guards,
        (string From, string Trigger) key,
        List<Observation> moves,
        List<Observation> refusals,
        List<string> messages,
        JsonObject edge,
        List<Problem> problems
    )
    {
        if (machine.Triggers[key.Trigger].Guard is { } named)
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
                        $"{key.Trigger} from {key.From} is decided by the guard {named}, and it "
                            + $"{(broken.Result is Result.Moved ? "fails where the trigger works" : "holds where the trigger is refused")} here."
                    )
                );
                return false;
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
                            ? $"{key.Trigger} from {key.From} is refused here and works in '{move.Scenario.Name}' ({move.Scenario.Id}), and no guard in the glossary tells the two apart."
                            : $"{key.Trigger} from {key.From} is refused here and works in '{move.Scenario.Name}' ({move.Scenario.Id}), and the guards {string.Join(", ", deciding.Select(g => g.Name))} all tell them apart. Add a scenario that separates them."
                    )
                );
                return false;
            }
            edge["guard"] = machine.Guards[deciding[0].Name].Rule.DeepClone();
            if (messages.Count == 1)
                edge["guardMessage"] = messages[0];
        }
        return true;
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
        var observations = walks
            .SelectMany(w => w.Observations)
            .Where(o => string.Equals(o.Machine, machine.Id, StringComparison.Ordinal))
            .ToList();
        foreach (
            var o in observations.Where(o =>
                o.Input is not null && served.Contains(o.Trigger, StringComparer.Ordinal)
            )
        )
            AddSample(samples, o.Trigger, o.Input.NotNull());
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
                AddSample(samples, $"{o.State}.done", example);
        }

        var seeds = Seeds(machine, observations);

        return Block(samples, seeds);
    }

    private static void AddSample(
        SortedDictionary<string, SortedDictionary<string, JsonNode>> into,
        string key,
        JsonNode value
    )
    {
        if (!into.TryGetValue(key, out var list))
            into[key] = list = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        list.TryAdd(CanonicalJson.Serialize(value), value);
    }

    // Every context a Given put an instance in before a When fired from it, but the initial state's initial context.
    private static SortedDictionary<string, SortedDictionary<string, JsonNode>> Seeds(
        GlossaryMachine machine,
        List<Observation> observations
    )
    {
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
                AddSample(seeds, o.From, o.Context);
        return seeds;
    }

    private static JsonObject Block(
        SortedDictionary<string, SortedDictionary<string, JsonNode>> samples,
        SortedDictionary<string, SortedDictionary<string, JsonNode>> seeds
    )
    {
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
                o.Value.NotNull()["edges"]
                    .NotNull()
                    .AsArray()
                    .Select(e =>
                        (
                            State: o.Value["state"].NotNull().GetValue<string>(),
                            Target: e.NotNull()["to"].NotNull().GetValue<string>()
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
            ReportMovesInto(machine, walks, edges, state, target, problems);
            ReportTimersInto(machine, walks, timers, state, target, problems);
        }
    }

    // Each ordinary move into an outcome's reserved target, but a self-loop, is a problem.
    private static void ReportMovesInto(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        IEnumerable<(string From, string Trigger, string To)> edges,
        string state,
        string target,
        List<Problem> problems
    )
    {
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
    }

    // Each timer into an outcome's reserved target is a problem.
    private static void ReportTimersInto(
        GlossaryMachine machine,
        IReadOnlyList<Walk> walks,
        JsonArray timers,
        string state,
        string target,
        List<Problem> problems
    )
    {
        foreach (
            var timer in timers.Where(t =>
                string.Equals(
                    t.NotNull()["target"].NotNull().GetValue<string>(),
                    target,
                    StringComparison.Ordinal
                )
            )
        )
        {
            var from = timer.NotNull()["state"].NotNull().GetValue<string>();
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
                if (OutcomeEdge(machine, invoke, outcome, observed, problems) is { } edge)
                    edges.Add(edge);
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

    // Where one outcome of an invoked train goes, or null when the scenarios disagree or never say.
    private static JsonObject? OutcomeEdge(
        GlossaryMachine machine,
        GlossaryInvoke invoke,
        GlossaryOutcome outcome,
        List<OutcomeObservation> observed,
        List<Problem> problems
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
            return null;
        }
        if (targets.Count == 0)
            return null;
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
        return edge;
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
            var shortest = fired.MinBy(o => o.Elapsed).NotNull();
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
            ReportTimerProblems(machine, state, targets[0], shortest, stays, problems);
            timers.Add(Timer(state, targets[0], shortest));
            shown[state] = shortest;
        }

        ReportTimerLoops(machine, shown, problems);
        return timers;
    }

    private static JsonObject Timer(string state, string target, TimerObservation shortest) =>
        new()
        {
            ["state"] = state,
            ["trigger"] = $"{state}.after",
            ["target"] = target,
            ["rules"] = new JsonArray(),
            ["after"] = IsoDuration.Format(shortest.Elapsed),
        };

    private static void ReportTimerLoops(
        GlossaryMachine machine,
        Dictionary<string, TimerObservation> shown,
        List<Problem> problems
    )
    {
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
    }

    // What the host refuses of a timer, said here: one too long, one shorter than a stay in its state, one into its
    // own state, and on a user's machine one into a state that runs a train.
    private static void ReportTimerProblems(
        GlossaryMachine machine,
        string state,
        string target,
        TimerObservation shortest,
        List<StayObservation> stays,
        List<Problem> problems
    )
    {
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
        if (string.Equals(target, state, StringComparison.Ordinal))
            problems.Add(
                new Problem(
                    shortest.Scenario.File,
                    shortest.Line,
                    $"Time takes {machine.Id} from {state} back into {state}, and a timer never enters its own state."
                )
            );
        if (!machine.IsSystem && machine.Invokes.ContainsKey(target))
            problems.Add(
                new Problem(
                    shortest.Scenario.File,
                    shortest.Line,
                    $"Time takes {machine.Id} into {target}, which runs a train, and a user's machine never starts a train on a timer: the host refuses it, since no user is there to run it as."
                )
            );
    }

    private static string Said(TimeSpan time) => IsoDuration.Format(time);

    // A train's output fields in the order the host reflects them from its output type (SchemaReflection: by name,
    // ordinal, at every depth), so the IR the host exports from the train is the IR written here, whatever order the
    // glossary lists them in.
    private static JsonArray ByName(JsonArray fields) =>
        new(
            fields
                .Select(f => (JsonObject)f.NotNull().DeepClone())
                .OrderBy(f => f["name"].NotNull().GetValue<string>(), StringComparer.Ordinal)
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
