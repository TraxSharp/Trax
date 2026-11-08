using System.Text.Json.Nodes;
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
        List<Problem> problems
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
        {
            var (when, enters, targetId) = group.Key;
            var first = group.First();
            Problem At(PolicyObservation o, string message) =>
                new(o.Scenario.File, o.Line, message);
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
                continue;
            }

            // The trigger: the one edge of the target that goes from where it was to where the scenario says.
            var triggers = group
                .Select(o =>
                    irs[targetId]["transitions"]!
                        .AsArray()
                        .Where(e =>
                            e!["from"]!.GetValue<string>() == o.From
                            && e["to"]!.GetValue<string>() == o.To
                        )
                        .Select(e => e!["trigger"]!.GetValue<string>())
                        .Distinct()
                        .ToList()
                )
                .ToList();
            if (triggers.Any(t => t.Count != 1))
            {
                var o = group.First(x => triggers[group.ToList().IndexOf(x)].Count != 1);
                problems.Add(
                    At(
                        o,
                        $"{targetId} goes from {o.From} to {o.To} when {when} enters {enters}, and {(triggers[group.ToList().IndexOf(o)].Count == 0 ? "no scenario shows a trigger doing that. Add one that does: 'When ... '" : "more than one trigger does that, so the policy's is not clear")}."
                    )
                );
                continue;
            }
            var trigger = triggers.SelectMany(t => t).Distinct().ToList();
            if (trigger.Count != 1)
            {
                problems.Add(
                    At(
                        first,
                        $"{targetId} reacts to {when} entering {enters} through more than one trigger ({string.Join(", ", trigger)}). A policy fires one."
                    )
                );
                continue;
            }
            if (byPerson.Contains((targetId, trigger[0])))
            {
                problems.Add(
                    At(
                        first,
                        $"{targetId}.{trigger[0]} is fired by a person in another scenario and by a policy here. A trigger the server fires is not a client's: make them two triggers."
                    )
                );
                continue;
            }

            // The host refuses a user's machine whose server-only trigger starts a train, as it refuses a timer that
            // does: no user is there to run it as.
            if (
                !target.IsSystem
                && irs[targetId]["transitions"]!
                    .AsArray()
                    .Select(e => e!)
                    .FirstOrDefault(e =>
                        e["trigger"]!.GetValue<string>() == trigger[0]
                        && target.Invokes.ContainsKey(e["to"]!.GetValue<string>())
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
                continue;
            }

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
                    ["trigger"] = trigger[0],
                };
            }
            else if (source.SystemKey is not null)
            {
                var waiting = group.Select(o => o.From).Distinct().ToList();
                if (waiting.Count != 1)
                {
                    problems.Add(
                        At(
                            first,
                            $"When {when} enters {enters}, {targetId} instances move from {string.Join(" and ", waiting)}. A broadcast reaches the instances waiting in one state."
                        )
                    );
                    continue;
                }
                policy["shape"] = "broadcast";
                policy["then"] = new JsonObject
                {
                    ["machine"] = targetId,
                    ["inState"] = waiting[0],
                    ["trigger"] = trigger[0],
                };
            }
            else
            {
                if (group.FirstOrDefault(o => o.TargetOwner != o.WhenOwner) is { } crossing)
                {
                    problems.Add(
                        At(
                            crossing,
                            $"{crossing.TargetOwner}'s {target.Reference} moves when {crossing.WhenOwner}'s {source.Reference} does. A policy acts on the same owner's instances; another owner's needs instances shared between people, which Trax does not have yet."
                        )
                    );
                    continue;
                }
                var links = target.Links.Values.Where(l => l.Machine == when).ToList();
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
                    continue;
                }
                policy["shape"] = "linked";
                policy["then"] = new JsonObject
                {
                    ["machine"] = targetId,
                    ["link"] = links[0].Field,
                    ["trigger"] = trigger[0],
                };
            }
            policies.Add(policy);
        }

        // A loop with no timer in it would have the server move instances forever. The host refuses the same loops
        // through the same check.
        var read = policies
            .Select(p => new HostPolicy(
                p["shape"]!.GetValue<string>(),
                p["when"]!["machine"]!.GetValue<string>(),
                p["when"]!["enters"]!.GetValue<string>(),
                p["then"]!["machine"]!.GetValue<string>(),
                p["then"]!["trigger"]!.GetValue<string>(),
                ((JsonObject)p["then"]!)
                    .Single(kv => kv.Key is "key" or "inState" or "link")
                    .Value!.GetValue<string>()
            ))
            .ToList();
        foreach (var loop in PolicyLoops.Find(read, irs))
            problems.Add(new Problem("glossary", 0, PolicyLoops.Problem(loop)));

        return new JsonArray(policies.Select(p => (JsonNode?)p).ToArray());
    }
}
