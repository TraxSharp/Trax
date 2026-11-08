using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A person of a scenario: <c>Ada is a customer</c>.</summary>
internal sealed record Person(string Name, GlossaryActor Actor);

/// <summary>What a host answered a request with, as a caller sees it.</summary>
internal abstract record Answer
{
    /// <summary>The request was taken: the instance is now in <see cref="State"/>.</summary>
    internal sealed record Moved(string State) : Answer;

    /// <summary>The request was refused with the code and message the host sends.</summary>
    internal sealed record Refused(RefusalCode Code, string Message) : Answer;

    /// <summary>The caller has no such operation: the host does not serve it to them.</summary>
    internal sealed record NotServed(string What) : Answer;
}

/// <summary>
/// What a scenario runs against (ADR <c>Trax.Docs/adr/0058</c>): the derived machines in memory, or a host. The runner
/// reads each step's meaning and asks the driver, so a scenario means the same against both. A Given arranges the
/// instance directly (<see cref="Seed"/>), as a test arranges a database: it fires nothing and sets off no policy.
/// </summary>
internal interface IScenarioDriver
{
    ValueTask<Answer> Start(Person person, string machine);

    /// <summary>Puts an instance in a state with a context, starting it first if it has none.</summary>
    ValueTask Seed(InstanceKey key, string state, JsonObject? context);

    /// <summary>An instance's state and context, or null if it has none.</summary>
    ValueTask<(string State, JsonObject Context)?> Read(InstanceKey key);

    /// <summary>Fires a trigger as <paramref name="person"/>, or as the server when that is null.</summary>
    ValueTask<Answer> Fire(Person? person, InstanceKey key, string trigger, JsonObject? input);

    /// <summary>Delivers an invoked train's outcome to the instance waiting in <paramref name="state"/>.</summary>
    ValueTask<Answer> Outcome(InstanceKey key, string state, string kind, JsonNode? output);

    ValueTask Pass(TimeSpan time);

    /// <summary>What one field of a view shows <paramref name="person"/>, or the refusal.</summary>
    ValueTask<(IReadOnlyList<string>? Values, Answer? Refused)> View(
        Person person,
        string view,
        string field
    );
}

internal enum ScenarioStatus
{
    Passed,
    Failed,

    /// <summary>A Given or When derivation cannot read: the scenario was not run.</summary>
    Undefined,

    /// <summary>A scenario tagged <c>@host</c>, which only the host bindings run.</summary>
    Skipped,
}

/// <summary>One step's result: <see cref="Message"/> says why it failed, or that it is about the UI and was skipped.</summary>
internal sealed record StepResult(FeatureStep Step, ScenarioStatus? Status, string? Message);

internal sealed record ScenarioResult(
    FeatureScenario Scenario,
    ScenarioStatus Status,
    IReadOnlyList<StepResult> Steps
)
{
    /// <summary>The first step that failed or could not be read.</summary>
    internal StepResult? Failure =>
        Steps.FirstOrDefault(s => s.Status is not ScenarioStatus.Passed and not null);

    /// <summary>Steps about the UI, which no driver here checks: <c>--strict</c> fails them.</summary>
    internal IEnumerable<StepResult> Skipped => Steps.Where(s => s.Status is null);
}

/// <summary>
/// Runs a scenario's steps against a driver: a Given arranges, a When asks, and a Then checks what the driver answers.
/// It stops at the first failing step. The derivation's own check is this runner over the machines it derived.
/// </summary>
internal static class ScenarioRunner
{
    internal static async ValueTask<ScenarioResult> Run(
        Walk walk,
        Glossary glossary,
        IScenarioDriver driver,
        bool failFast = true
    )
    {
        var scenario = walk.Scenario;
        var meanings = walk.Steps.ToDictionary(s => s.Step, s => s.Meaning);
        var results = new List<StepResult>();

        // A Given or When with no meaning, or any step the walk refused, means the scenario does not read: it is not
        // run, as derivation would not derive from it.
        var undefined =
            scenario.Steps.FirstOrDefault(s =>
                (s.Kind != StepKind.Then && !meanings.ContainsKey(s))
                || walk.Problems.Any(p => p.Line == s.Line)
            ) ?? (walk.Problems.Count > 0 ? scenario.Steps.FirstOrDefault() : null);
        if (undefined is not null)
        {
            var why =
                walk.Problems.FirstOrDefault(p => p.Line == undefined.Line)?.Message
                ?? walk.Problems.FirstOrDefault()?.Message;
            foreach (var step in scenario.Steps)
                results.Add(
                    step == undefined
                        ? new StepResult(step, ScenarioStatus.Undefined, why)
                        : new StepResult(step, null, "not run")
                );
            return new ScenarioResult(scenario, ScenarioStatus.Undefined, results);
        }

        var session = new ScenarioSession(glossary, driver);
        var failed = false;

        foreach (var step in scenario.Steps)
        {
            if (failed && failFast)
            {
                results.Add(new StepResult(step, null, "not run"));
                continue;
            }
            if (!meanings.TryGetValue(step, out var meaning))
            {
                results.Add(new StepResult(step, null, "about the UI: not checked here"));
                continue;
            }
            var failure = await session.Step(step, meaning);
            results.Add(
                new StepResult(
                    step,
                    failure is null ? ScenarioStatus.Passed : ScenarioStatus.Failed,
                    failure
                )
            );
            failed |= failure is not null;
        }
        return new ScenarioResult(
            scenario,
            failed ? ScenarioStatus.Failed : ScenarioStatus.Passed,
            results
        );
    }

    internal static string Describe(Answer? answer) =>
        answer switch
        {
            Answer.Moved m => $"taken, moving it to {m.State}",
            Answer.Refused r => $"{r.Code.ToWire()} \"{r.Message}\"",
            Answer.NotServed n => $"not served ({n.What})",
            _ => "nothing",
        };
}

/// <summary>
/// One scenario as it runs, a step at a time: the people it has introduced, the instances it named, and the last
/// answer, which its Thens check. The runner and the Reqnroll bindings both step through one.
/// </summary>
internal sealed class ScenarioSession(Glossary glossary, IScenarioDriver driver)
{
    private readonly Dictionary<string, Person> people = new(StringComparer.Ordinal);
    private readonly List<InstanceKey> known = [];
    private (InstanceKey Key, Person? By, Answer Answer)? last;

    /// <summary>The people the scenario has introduced, by name, as the step resolver reads them.</summary>
    internal IReadOnlyDictionary<string, string> People =>
        people.ToDictionary(p => p.Key, p => p.Value.Actor.Name);

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
                return null;

            case (StepKind.Given, Meaning.Start s):
            {
                var key = new InstanceKey(s.Machine, s.Person);
                Know(key);
                var answer = await driver.Start(people[s.Person], s.Machine);
                return answer is Answer.Moved
                    ? null
                    : $"{s.Person} could not start it: {ScenarioRunner.Describe(answer)}.";
            }

            case (StepKind.Given, Meaning.InState s):
            {
                var key = new InstanceKey(s.Machine, s.Owner, s.Key);
                Know(key);
                var context = (await driver.Read(key))?.Context;
                await driver.Seed(key, s.State, context);
                return null;
            }

            case (StepKind.Given, Meaning.Guard g):
            {
                var key = new InstanceKey(g.Machine, g.Owner, g.Key);
                Know(key);
                // An instance no step has started yet is arranged from its initial state and context, as the walk
                // reads it; a later Given puts it in the state the scenario names.
                if (await driver.Read(key) is null)
                    await driver.Seed(key, glossary.Machines[g.Machine].Initial, null);
                var now =
                    await driver.Read(key)
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
                await driver.Seed(key, now.State, context);
                return null;
            }

            case (StepKind.When, Meaning.Act a):
            {
                var key = new InstanceKey(a.Machine, a.Owner);
                Know(key);
                var actor = people[a.Person];
                last = (key, actor, await driver.Fire(actor, key, a.Trigger, a.Input));
                return null;
            }

            case (StepKind.When, Meaning.Happens h):
            {
                var key = new InstanceKey(h.Machine, h.Owner, h.Key);
                Know(key);
                last = (key, null, await driver.Fire(null, key, h.Trigger, h.Input));
                return null;
            }

            case (StepKind.When, Meaning.Outcome o):
            {
                InstanceKey? waiting = null;
                foreach (var key in known.Where(k => k.Machine == o.Machine))
                    if ((await driver.Read(key))?.State == o.State)
                    {
                        waiting = key;
                        break;
                    }
                if (waiting is null)
                    return $"no {o.Machine} is in {o.State} to be told '{o.Phrase}'.";
                var invoke = glossary.Machines[o.Machine].Invokes[o.State];
                last = (
                    waiting,
                    null,
                    await driver.Outcome(
                        waiting,
                        o.State,
                        invoke.Outcomes[o.Phrase].Kind,
                        invoke.Examples.GetValueOrDefault(o.Phrase)?.DeepClone()
                    )
                );
                return null;
            }

            case (StepKind.When, Meaning.Passes p):
                await driver.Pass(p.Time);
                last = null;
                return null;

            case (StepKind.Then, Meaning.InState s):
            {
                var key = new InstanceKey(s.Machine, s.Owner, s.Key);
                if (last is { } l && l.Key == key && l.Answer is not Answer.Moved && !s.Still)
                    return $"'{step.Text}', and it was refused: {ScenarioRunner.Describe(l.Answer)}.";
                var now = await driver.Read(key);
                return now?.State == s.State
                    ? null
                    : $"'{step.Text}', and it is {(now is null ? "not started" : $"in {now.Value.State}")}.";
            }

            case (StepKind.Then, Meaning.ViewShows v):
            {
                var (values, refused) = await driver.View(people[v.Person], v.View, v.Field);
                if (refused is not null)
                    return $"'{step.Text}', and {v.Person} may not read it: {ScenarioRunner.Describe(refused)}.";
                return values!.Contains(v.Value)
                    ? null
                    : $"'{step.Text}', and it shows {(values!.Count == 0 ? "nothing" : string.Join(" or ", values.Select(x => $"\"{x}\"")))}.";
            }

            case (StepKind.Then, Meaning.Told t):
                return
                    last?.Answer is Answer.Refused { Code: RefusalCode.GuardFailed } told
                    && told.Message == t.Message
                    ? null
                    : $"'{step.Text}', and the answer was {ScenarioRunner.Describe(last?.Answer)}.";

            case (StepKind.Then, Meaning.Denied d):
                return
                    last is { By: { } by } denied
                    && by.Name == d.Person
                    && denied.Answer
                        is Answer.NotServed
                            or Answer.Refused { Code: RefusalCode.Forbidden }
                    ? null
                    : $"'{step.Text}', and the answer was {ScenarioRunner.Describe(last?.Answer)}.";

            case (StepKind.Then, Meaning.Guard g):
            {
                var now = await driver.Read(new InstanceKey(g.Machine, g.Owner, g.Key));
                if (now is null)
                    return $"'{step.Text}', and it is not started.";
                var rule = glossary.Machines[g.Machine].Guards[g.Name].Rule;
                return
                    RuleEvaluator.Evaluate(
                        IrMachine.ReadRule(rule),
                        now.Value.Context,
                        null,
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
