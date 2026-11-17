using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// One scenario as it runs, a step at a time: the people it has introduced, the instances it named, and the last
/// answer, which its Thens check. The runner and the Reqnroll bindings both step through one.
/// </summary>
internal sealed class ScenarioSession(Glossary glossary, IScenarioDriver driver)
{
    private readonly Dictionary<string, Person> people = new(StringComparer.Ordinal);
    private readonly List<InstanceKey> known = [];
    private (InstanceKey Key, Person? By, Answer Answer)? last;

    // Per instance, how far the Thens since the last When have read into the states it entered: the next Then naming
    // one of them reads on from here, so the Thens follow the cascade in order.
    private readonly Dictionary<InstanceKey, int> cursors = [];

    /// <summary>
    /// Starts a When: the cascade the Thens after it read is this When's alone. A test project's own When starts one too.
    /// </summary>
    internal async ValueTask BeginWhen()
    {
        cursors.Clear();
        await driver.BeginWhen().ConfigureAwait(false);
    }

    /// <summary>The people the scenario has introduced, by name, as the step resolver reads them.</summary>
    internal IReadOnlyDictionary<string, string> People =>
        people.ToDictionary(p => p.Key, p => p.Value.Actor.Name, StringComparer.Ordinal);

    private void Know(InstanceKey key)
    {
        if (!known.Contains(key))
            known.Add(key);
    }

    /// <summary>Runs one step: null when it held, or why it did not.</summary>
    internal async ValueTask<string?> Step(FeatureStep step, Meaning meaning)
    {
        switch (step.Kind, meaning)
        {
            case (_, Meaning.Named n):
                people[n.Name] = new Person(n.Name, glossary.Actors[n.Actor]);
                driver.Introduce(people[n.Name]);
                return null;

            case (StepKind.Given, Meaning.Start s):
            {
                var key = new InstanceKey(s.Machine, s.Person);
                Know(key);
                var answer = await driver.Start(people[s.Person], s.Machine).ConfigureAwait(false);
                return answer is Answer.Moved
                    ? null
                    : $"{s.Person} could not start it: {ScenarioRunner.Describe(answer)}.";
            }

            case (StepKind.Given, Meaning.InState s):
            {
                var key = new InstanceKey(s.Machine, s.Owner, s.Key);
                Know(key);
                var context = (await driver.Read(key).ConfigureAwait(false))?.Context;
                await driver.Seed(key, s.State, context).ConfigureAwait(false);
                return null;
            }

            case (StepKind.Given, Meaning.Guard g):
            {
                var key = new InstanceKey(g.Machine, g.Owner, g.Key);
                Know(key);
                // An instance no step has started yet is arranged from its initial state and context, as the walk
                // reads it; a later Given puts it in the state the scenario names.
                if (await driver.Read(key).ConfigureAwait(false) is null)
                    await driver
                        .Seed(key, glossary.Machines[g.Machine].Initial, context: null)
                        .ConfigureAwait(false);
                var now =
                    await driver.Read(key).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"{key.Machine} could not be arranged.");
                var context = Satisfy.Context(
                    glossary.Machines[g.Machine],
                    g.Name,
                    g.Holds,
                    now.Context,
                    out var why
                );
                if (context is null)
                    return why ?? $"nothing sets up '{g.Name}'.";
                await driver.Seed(key, now.State, context).ConfigureAwait(false);
                return null;
            }

            case (StepKind.When, Meaning.Act a):
            {
                var key = new InstanceKey(a.Machine, a.Owner);
                Know(key);
                var actor = people[a.Person];
                await BeginWhen().ConfigureAwait(false);
                last = (
                    key,
                    actor,
                    await driver.Fire(actor, key, a.Trigger, a.Input).ConfigureAwait(false)
                );
                return null;
            }

            case (StepKind.When, Meaning.Happens h):
            {
                var key = new InstanceKey(h.Machine, h.Owner, h.Key);
                Know(key);
                await BeginWhen().ConfigureAwait(false);
                last = (
                    key,
                    null,
                    await driver.Fire(person: null, key, h.Trigger, h.Input).ConfigureAwait(false)
                );
                return null;
            }

            case (StepKind.When, Meaning.Outcome o):
            {
                InstanceKey? waiting = null;
                foreach (
                    var key in known.Where(k =>
                        string.Equals(k.Machine, o.Machine, StringComparison.Ordinal)
                    )
                )
                    if (
                        string.Equals(
                            (await driver.Read(key).ConfigureAwait(false))?.State,
                            o.State,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        waiting = key;
                        break;
                    }
                if (waiting is null)
                    return $"no {o.Machine} is in {o.State} to be told '{o.Phrase}'.";
                var invoke = glossary.Machines[o.Machine].Invokes[o.State];
                await BeginWhen().ConfigureAwait(false);
                last = (
                    waiting,
                    null,
                    await driver
                        .Outcome(
                            waiting,
                            o.State,
                            invoke.Outcomes[o.Phrase].Kind,
                            invoke.Examples.GetValueOrDefault(o.Phrase)?.DeepClone()
                        )
                        .ConfigureAwait(false)
                );
                return null;
            }

            case (StepKind.When, Meaning.Passes p):
                await BeginWhen().ConfigureAwait(false);
                await driver.Pass(p.Time).ConfigureAwait(false);
                last = null;
                return null;

            case (StepKind.Then, Meaning.InState s):
            {
                var key = new InstanceKey(s.Machine, s.Owner, s.Key);
                if (last is { } l && l.Key == key && l.Answer is not Answer.Moved && !s.Still)
                    return $"'{step.Text}', and it was refused: {ScenarioRunner.Describe(l.Answer)}.";
                var now = await driver.Read(key).ConfigureAwait(false);
                // The Thens after a When follow the cascade in order: one may name where the When took the instance
                // before a policy it set off moved it on, but not a state an earlier Then has read past, nor one a
                // previous When's cascade entered. A still-Then says it did not move, so only where it is counts.
                if (!s.Still)
                {
                    var cascade = await driver.Cascaded(key).ConfigureAwait(false);
                    var from = cursors.GetValueOrDefault(key);
                    for (var i = from; i < cascade.Count; i++)
                        if (string.Equals(cascade[i], s.State, StringComparison.Ordinal))
                        {
                            cursors[key] = i + 1;
                            return null;
                        }
                }
                if (string.Equals(now?.State, s.State, StringComparison.Ordinal))
                    return null;
                return $"'{step.Text}', and it is {(now is null ? "not started" : $"in {now.Value.State}")}.";
            }

            case (StepKind.Then, Meaning.ViewShows v):
            {
                var (values, refused) = await driver
                    .View(people[v.Person], v.View, v.Field)
                    .ConfigureAwait(false);
                if (refused is not null)
                    return $"'{step.Text}', and {v.Person} may not read it: {ScenarioRunner.Describe(refused)}.";
                return values!.Contains(v.Value, StringComparer.Ordinal)
                    ? null
                    : $"'{step.Text}', and it shows {(values!.Count == 0 ? "nothing" : string.Join(" or ", values.Select(x => $"\"{x}\"")))}.";
            }

            case (StepKind.Then, Meaning.Told t):
                return
                    last?.Answer is Answer.Refused { Code: RefusalCode.GuardFailed } told
                    && string.Equals(told.Message, t.Message, StringComparison.Ordinal)
                    ? null
                    : $"'{step.Text}', and the answer was {ScenarioRunner.Describe(last?.Answer)}.";

            case (StepKind.Then, Meaning.Denied d):
                return
                    last is { By: { } by } denied
                    && string.Equals(by.Name, d.Person, StringComparison.Ordinal)
                    && denied.Answer
                        is Answer.NotServed
                            or Answer.Refused { Code: RefusalCode.Forbidden }
                    ? null
                    : $"'{step.Text}', and the answer was {ScenarioRunner.Describe(last?.Answer)}.";

            case (StepKind.Then, Meaning.Guard g):
            {
                var now = await driver
                    .Read(new InstanceKey(g.Machine, g.Owner, g.Key))
                    .ConfigureAwait(false);
                if (now is null)
                    return $"'{step.Text}', and it is not started.";
                var rule = glossary.Machines[g.Machine].Guards[g.Name].Rule;
                return
                    RuleEvaluator.Evaluate(
                        IrMachine.ReadRule(rule),
                        now.Value.Context,
                        input: null,
                        glossary.Machines[g.Machine].ExampleRules
                    ) == g.Holds
                    ? null
                    : $"'{step.Text}' does not hold.";
            }

            default:
                return $"'{step.Text}' is not something a {step.Kind} step can say.";
        }
    }
}
