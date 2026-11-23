using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Merges what every scenario shows one instance setting off in another into the host file's policies (ADR
/// <c>Trax.Docs/adr/0058</c>). A policy waits for one machine to enter a state and fires a trigger only the server fires
/// on another. The trigger is the one edge of the target's derived machine that goes where the scenarios say, so a
/// scenario elsewhere must show it happening. The shape follows from the owners: a system machine reaching users'
/// instances is a broadcast to every one in the state, anything reaching a system machine reaches it at its key, and
/// one user's machine reaching another of theirs needs a link in the glossary, since the same owner alone is
/// ambiguous.
/// </summary>
internal static class PolicyMerge
{
    internal static JsonArray Derive(
        Glossary glossary,
        IReadOnlyList<Walk> walks,
        IReadOnlyDictionary<string, JsonObject> irs,
        List<Problem> problems,
        List<Problem> warnings
    )
    {
        var policies = new List<JsonObject>();
        var observed = walks.SelectMany(w => w.Policies).ToList();
        var byPerson = walks
            .SelectMany(w => w.Observations)
            .Where(o => o.Person is not null)
            .Select(o => (o.Machine, o.Trigger))
            .ToHashSet();

        foreach (
            var group in observed
                .GroupBy(o => (o.When, o.Enters, o.Target))
                .OrderBy(g => g.Key.When, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Enters, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Target, StringComparer.Ordinal)
        )
            if (Policy(glossary, irs, byPerson, group, problems) is { } policy)
                policies.Add(policy);

        ReportLoopsAndRaces(observed, policies, irs, problems, warnings);

        return new JsonArray(policies.Cast<JsonNode?>().ToArray());
    }

    private static Problem At(PolicyObservation o, string message) =>
        new(o.Scenario.File, o.Line, message);

    // One policy, from every scenario that shows the same instance entering the same state moving the same machine;
    // null when they do not make one (a problem says why).
    private static JsonObject? Policy(
        Glossary glossary,
        IReadOnlyDictionary<string, JsonObject> irs,
        HashSet<(string Machine, string Trigger)> byPerson,
        IGrouping<(string When, string Enters, string Target), PolicyObservation> group,
        List<Problem> problems
    )
    {
        var (when, enters, targetId) = group.Key;
        var first = group.First();
        var source = glossary.Machines[when];
        var target = glossary.Machines[targetId];

        // A keyed system machine has an instance per key, started and moved by host code. No policy picks one out,
        // and none is set off by one: the host code that moves it moves what follows.
        if (source.KeyedBy is not null || target.KeyedBy is not null)
        {
            var keyed = source.KeyedBy is not null ? when : targetId;
            problems.Add(
                At(
                    first,
                    $"{targetId} moves when {when} enters {enters}, and {keyed} is a keyed system machine, which host code moves: no policy reacts to it or moves it. Show each move as its own When."
                )
            );
            return null;
        }
        if (PolicyTrigger(irs, byPerson, group, first, target, problems) is not { } trigger)
            return null;

        var policy = new JsonObject
        {
            ["when"] = new JsonObject { ["machine"] = when, ["enters"] = enters },
        };
        if (target.SystemKey is { } key)
        {
            policy["shape"] = "toSystem";
            policy["then"] = new JsonObject
            {
                ["machine"] = targetId,
                ["key"] = key,
                ["trigger"] = trigger,
            };
        }
        else if (source.SystemKey is not null)
        {
            if (!Broadcast(group, first, policy, trigger, problems))
                return null;
        }
        else if (!Linked(group, first, source, target, policy, trigger, problems))
            return null;
        return policy;
    }

    // The one trigger the policy fires; null when the scenarios do not show exactly one, or show it fired by a person,
    // or show it starting a train on a user's machine.
    private static string? PolicyTrigger(
        IReadOnlyDictionary<string, JsonObject> irs,
        HashSet<(string Machine, string Trigger)> byPerson,
        IGrouping<(string When, string Enters, string Target), PolicyObservation> group,
        PolicyObservation first,
        GlossaryMachine target,
        List<Problem> problems
    )
    {
        var (when, enters, targetId) = group.Key;
        if (Triggers(irs, group, first, problems) is not { } trigger)
            return null;

        if (byPerson.Contains((targetId, trigger[0])))
        {
            problems.Add(
                At(
                    first,
                    $"{targetId}.{trigger[0]} is fired by a person in another scenario and by a policy here. A trigger the server fires is not a client's: make them two triggers."
                )
            );
            return null;
        }

        // The host refuses a user's machine whose server-only trigger starts a train, as it refuses a timer that
        // does: no user is there to run it as.
        if (
            !target.IsSystem
            && irs[targetId]
                ["transitions"]
                .NotNull()
                .AsArray()
                .Select(e => e.NotNull())
                .FirstOrDefault(e =>
                    string.Equals(
                        e["trigger"].NotNull().GetValue<string>(),
                        trigger[0],
                        StringComparison.Ordinal
                    ) && target.Invokes.ContainsKey(e["to"].NotNull().GetValue<string>())
                )
                is { } invoking
        )
        {
            problems.Add(
                At(
                    first,
                    $"{when} entering {enters} moves {targetId} into {invoking["to"]}, which runs a train, and a user's machine never starts a train on a policy: the host refuses it, since no user is there to run it as. Let a person's trigger enter {invoking["to"]}."
                )
            );
            return null;
        }
        return trigger[0];
    }

    // The triggers of the target's edges that go where the scenarios say; null unless there is exactly one.
    private static List<string>? Triggers(
        IReadOnlyDictionary<string, JsonObject> irs,
        IGrouping<(string When, string Enters, string Target), PolicyObservation> group,
        PolicyObservation first,
        List<Problem> problems
    )
    {
        var (when, enters, targetId) = group.Key;
        // The trigger: the one edge of the target that goes from where it was to where the scenario says.
        var triggers = group
            .Select(o =>
                irs[targetId]
                    ["transitions"]
                    .NotNull()
                    .AsArray()
                    .Where(e =>
                        string.Equals(
                            e.NotNull()["from"].NotNull().GetValue<string>(),
                            o.From,
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            e["to"].NotNull().GetValue<string>(),
                            o.To,
                            StringComparison.Ordinal
                        )
                    )
                    .Select(e => e.NotNull()["trigger"].NotNull().GetValue<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            )
            .ToList();
        if (triggers.Exists(t => t.Count != 1))
        {
            var o = group.First(x => triggers[group.ToList().IndexOf(x)].Count != 1);
            problems.Add(
                At(
                    o,
                    $"{targetId} goes from {o.From} to {o.To} when {when} enters {enters}, and {(triggers[group.ToList().IndexOf(o)].Count == 0 ? "no scenario shows a trigger doing that. Add one that does: 'When ... '" : "more than one trigger does that, so the policy's is not clear")}."
                )
            );
            return null;
        }
        var trigger = triggers.SelectMany(t => t).Distinct(StringComparer.Ordinal).ToList();
        if (trigger.Count != 1)
        {
            problems.Add(
                At(
                    first,
                    $"{targetId} reacts to {when} entering {enters} through more than one trigger ({string.Join(", ", trigger)}). A policy fires one."
                )
            );
            return null;
        }
        return trigger;
    }

    // A system machine reaching users' instances: every one waiting in the state. False when they wait in several.
    private static bool Broadcast(
        IGrouping<(string When, string Enters, string Target), PolicyObservation> group,
        PolicyObservation first,
        JsonObject policy,
        string trigger,
        List<Problem> problems
    )
    {
        var (when, enters, targetId) = group.Key;
        var waiting = group.Select(o => o.From).Distinct(StringComparer.Ordinal).ToList();
        if (waiting.Count != 1)
        {
            problems.Add(
                At(
                    first,
                    $"When {when} enters {enters}, {targetId} instances move from {string.Join(" and ", waiting)}. A broadcast reaches the instances waiting in one state."
                )
            );
            return false;
        }
        policy["shape"] = "broadcast";
        policy["then"] = new JsonObject
        {
            ["machine"] = targetId,
            ["inState"] = waiting[0],
            ["trigger"] = trigger,
        };
        return true;
    }

    // One user's machine reaching another of theirs, through the glossary's one link. False when the owners differ or
    // there is not exactly one link.
    private static bool Linked(
        IGrouping<(string When, string Enters, string Target), PolicyObservation> group,
        PolicyObservation first,
        GlossaryMachine source,
        GlossaryMachine target,
        JsonObject policy,
        string trigger,
        List<Problem> problems
    )
    {
        var (when, _, targetId) = group.Key;
        if (
            group.FirstOrDefault(o =>
                !string.Equals(o.TargetOwner, o.WhenOwner, StringComparison.Ordinal)
            ) is
            { } crossing
        )
        {
            problems.Add(
                At(
                    crossing,
                    $"{crossing.TargetOwner}'s {target.Reference} moves when {crossing.WhenOwner}'s {source.Reference} does. A policy acts on the same owner's instances; another owner's needs instances shared between people, which Trax does not have yet."
                )
            );
            return false;
        }
        var links = target
            .Links.Values.Where(l => string.Equals(l.Machine, when, StringComparison.Ordinal))
            .ToList();
        if (links.Count != 1)
        {
            problems.Add(
                At(
                    first,
                    links.Count == 0
                        ? $"{targetId} moves when {when} does, and the same owner alone does not say which {target.Reference}: an owner may hold several. Add a link to the glossary's {targetId}: a context field holding the {source.Reference}'s id."
                        : $"{targetId} has more than one link to {when}, so which instance a policy reaches is not clear."
                )
            );
            return false;
        }
        policy["shape"] = "linked";
        policy["then"] = new JsonObject
        {
            ["machine"] = targetId,
            ["link"] = links[0].Field,
            ["trigger"] = trigger,
        };
        return true;
    }

    private static void ReportLoopsAndRaces(
        List<PolicyObservation> observed,
        List<JsonObject> policies,
        IReadOnlyDictionary<string, JsonObject> irs,
        List<Problem> problems,
        List<Problem> warnings
    )
    {
        // A loop with no timer in it would have the server move instances forever. The host refuses the same loops
        // through the same check.
        var read = policies
            .Select(p => new HostPolicy(
                p["shape"].NotNull().GetValue<string>(),
                p["when"].NotNull()["machine"].NotNull().GetValue<string>(),
                p["when"].NotNull()["enters"].NotNull().GetValue<string>(),
                p["then"].NotNull()["machine"].NotNull().GetValue<string>(),
                p["then"].NotNull()["trigger"].NotNull().GetValue<string>(),
                ((JsonObject)p["then"].NotNull())
                    .Single(kv => kv.Key is "key" or "inState" or "link")
                    .Value.NotNull()
                    .GetValue<string>()
            ))
            .ToList();
        foreach (var loop in PolicyLoops.Find(read, irs))
            problems.Add(new Problem(Problem.Glossary, 0, PolicyLoops.Problem(loop)));

        // A policy that waits for another's result is green in memory, where a When plays its cascade in the Thens'
        // order, and can miss on the host, where each policy fire and each train runs on its own. A warning, at the
        // scenario that showed the later policy, not a refusal: host code may still keep the order.
        foreach (var race in PolicyLoops.Races(read, irs))
        {
            var shown = observed.First(o =>
                string.Equals(o.When, race.Later.When, StringComparison.Ordinal)
                && string.Equals(o.Enters, race.Later.Enters, StringComparison.Ordinal)
                && string.Equals(o.Target, race.Later.Target, StringComparison.Ordinal)
            );
            warnings.Add(new Problem(shown.Scenario.File, shown.Line, PolicyLoops.Warning(race)));
        }
    }
}
