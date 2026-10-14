---
title: Write a decider
description: "Implement IDecider, register it, write policy as a RuleDecider, put a slower decider behind a fast one, and refuse questions at startup."
type: how-to
area: trains
nav_order: 5
source:
  - Trax.Core/src/Trax.Core/Decisions/IDecider.cs
  - Trax.Core/src/Trax.Core/Decisions/RuleDecider.cs
  - Trax.Core/src/Trax.Core/Decisions/CascadingDecider.cs
  - Trax.Core/src/Trax.Core/Decisions/IVetsQuestions.cs
---

# Write a decider

A decider answers the questions a `Decide`, `Switch`, `Gate` or `Scale` asks. Trax ships a
System One adapter for typed decision models (see [Decision Recording and Models](/docs/effect/decisions)),
`RuleDecider` for policy, `CascadingDecider` to combine two, and `ScriptedDecider` for tests.
Write your own when the answer comes from somewhere else. [Decisions](/docs/core/decisions)
explains how a chain asks and routes.

## Implement IDecider

Every type is in `Trax.Core.Decisions`, in the `Trax.Core` package, which Trax.Effect brings in:

```csharp fragment
public interface IDecider
{
    Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken);
}

public sealed record DecisionRequest(string Train, object State, IReadOnlyList<Question> Questions);

public abstract record Question(string Key, string Instructions);
public sealed record ChoiceQuestion(string Key, string Instructions, IReadOnlyList<Criterion> Options) : Question(Key, Instructions);
public sealed record ScoreQuestion(string Key, string Instructions, IReadOnlyList<Criterion> Levels) : Question(Key, Instructions);
public sealed record YesNoQuestion(string Key, string Instructions, string? Yes, string? No) : Question(Key, Instructions);
public sealed record Criterion(string Name, string? Description);

public sealed record DecisionResult(IReadOnlyDictionary<string, Answer> Answers);
public abstract record Answer { public string? Model { get; init; } }
public sealed record ChoiceAnswer(string Choice, double Confidence = 1.0, IReadOnlyDictionary<string, double>? Probabilities = null) : Answer;
public sealed record ScoreAnswer(double Score, double Confidence = 1.0, IReadOnlyList<double>? Probabilities = null) : Answer;
public sealed record YesNoAnswer(double Probability) : Answer;
```

Return one answer per question, keyed by `Question.Key`, of the kind the question asks for:

| Question | Answer |
|---|---|
| `ChoiceQuestion` | A `ChoiceAnswer` naming one of the options' `Criterion.Name` (the enum member's name), with a confidence from 0 to 1. |
| `ScoreQuestion` | A `ScoreAnswer` from 0 (the first level) to one less than the number of levels. |
| `YesNoQuestion` | A `YesNoAnswer` with the probability of yes. |

Set `Model` to the model and version that answered, or leave it null for a decider that is not a
model. `State` is the object in Memory, typed as `object`: match on its type, and do not change it.

```csharp compile=decider
using Trax.Core.Decisions;

public sealed class TicketRules : IDecider
{
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        var ticket = (Ticket)request.State;
        var refund = ticket.Message.Contains("refund", StringComparison.OrdinalIgnoreCase);

        var answers = request.Questions.ToDictionary(
            q => q.Key,
            q => q switch
            {
                ChoiceQuestion => (Answer)new ChoiceAnswer(refund ? "Refund" : "SelfServe", 0.9),
                YesNoQuestion => new YesNoAnswer(refund ? 0.8 : 0.1),
                _ => throw new InvalidOperationException($"Cannot answer {q.Key}."),
            });

        return Task.FromResult(new DecisionResult(answers));
    }
}
```

An answer that does not fit, or a question left unanswered, fails the run
([When the run fails instead](/docs/core/decisions#when-the-run-fails-instead)); throwing fails it
with your exception.

For runs that should replay, answer from the state alone. Replay checks the hash of the state and
nothing else, so an answer that depended on something the decider looked up elsewhere is replayed
even after that changed.

## Register it

One registration serves every decision in every train, at any lifetime:

```csharp compile=decider
using Trax.Core.Decisions;

services.AddSingleton<IDecider, TicketRules>();
```

To use a different decider for one step, register it and name it on that step with
`Decide(q => q.DecidedBy<TDecider>())`, or hand an instance to the chain with
[AddServices](/docs/sdk-reference/train-methods/add-services).

## Write policy as rules

When the decision is policy rather than judgement, write it as a `RuleDecider`. Each rule maps the
state to an enum member (`Choice`, `Score`) or a bool (`YesNo`), and its answers are always
certain:

```csharp compile
using Trax.Core.Decisions;

services.AddSingleton<IDecider>(
    new RuleDecider().Choice<LoanApplication, Underwriting>(application =>
        application switch
        {
            { CreditScore: >= 740, DebtToIncome: <= 0.36m } => Underwriting.Approve,
            { CreditScore: < 580 } or { DebtToIncome: > 0.5m } => Underwriting.Decline,
            _ => Underwriting.ManualReview,
        }
    )
);
```

## Escalate to a second decider

`CascadingDecider` asks the first decider every question and the second only the ones the first
was unsure of. Here the first is a Nimble model registered under a name, and the second is
`LargeModelDecider`, an `IDecider` of your own over a chat model (Trax does not ship one):

```csharp compile
using Trax.Core.Decisions;
using Trax.Effect.Decisions.SystemOne;
using Trax.Effect.Decisions.SystemOne.Extensions;
using Trax.Effect.Extensions;

services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects.AddNimbleDecider(
            "nimble",
            o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone")
        )
    )
);

services.AddSingleton<LargeModelDecider>();
services.AddSingleton<IDecider>(sp => new CascadingDecider(
    first: sp.GetRequiredKeyedService<SystemOneDecider>("nimble"),
    then: sp.GetRequiredService<LargeModelDecider>(),
    escalateBelow: 0.8
));
```

| Parameter | Default | Meaning |
|---|---|---|
| `escalateBelow` | 0.8 | A choice or score whose confidence is below it is escalated. |
| `unsureAbove`, `unsureBelow` | 0.2, 0.8 | A yes/no whose probability lies strictly between the two is escalated. |
| `logger` | null | An `ILogger` told when the first decider throws. |

Also escalated, whatever the bounds: a confidence or probability that is not a number, a question
the first decider did not answer, and an answer that does not fit its question (an option it was
not offered, a score off the levels, a confidence or probability outside 0 to 1, another kind of
answer). Only those questions are sent to the second decider, and its answer replaces the first.
If the first decider throws, every question goes to the second; cancellation passes straight
through. If the second fails, the run fails. Each bound must lie between 0 and 1, with
`unsureAbove` not above `unsureBelow`, or the constructor throws `ArgumentException`.

## Refuse questions at startup

A decider that can tell from the declaration alone that it cannot answer a question implements
`IVetsQuestions`. When a chain is read, each decider a step names (the live one and each shadow)
is looked up as the run would find it, among the services handed to `AddServices` and then in the
container, and shown the step's questions as `DeclaredQuestions` (`Train`, `Step`, the state's
declared type as `State`, and `Questions`). Each problem it returns refuses the chain, so the
[startup check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
reports it before the host takes traffic:

```csharp compile
using Trax.Core.Decisions;

public sealed class SmallModelDecider : IDecider, IVetsQuestions
{
    private const int MaxOptions = 8;

    public IEnumerable<string> Problems(DeclaredQuestions declared) =>
        declared
            .Questions.OfType<ChoiceQuestion>()
            .Where(q => q.Options.Count > MaxOptions)
            .Select(q => $"'{q.Key}' offers {q.Options.Count} options; this model takes {MaxOptions}.");

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
```

No decider is asked to decide while the chain is read. A decider the container can build only
inside a request scope is not vetted, and refuses at run time instead. `CascadingDecider` vets
with each of its tiers.

## SDK Reference

> [Decide](/docs/sdk-reference/train-methods/decide) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider) | [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider)
