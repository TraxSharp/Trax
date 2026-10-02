---
layout: default
title: Decide
description: Reference for Decide, which asks a decider several typed questions about one value in Memory in a single call and stores each answer as a typed decision.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 10
---

# Decide

Asks a decider several typed questions about one value in Memory, in a single call, and puts
each answer in Memory as a typed decision. Later [Switch](/docs/sdk-reference/train-methods/switch),
[Gate](/docs/sdk-reference/train-methods/gate) and [Scale](/docs/sdk-reference/train-methods/scale)
steps route on those decisions without asking again, and a junction can take one as its input.
See [Decisions](/docs/core/decisions) for the concepts.

## Decide\<TState\>(questions)

```csharp
protected MonadTask<TInput, TReturn> Decide<TState>(
    Func<Questions<TState>, Questions<TState>> questions
)
```

| Type Parameter | Description |
|---|---|
| `TState` | The value in Memory the questions are about. It is sent to the decider as the state. |

### Questions\<TState\>

| Method | Asks | Puts in Memory |
|---|---|---|
| `Choice<TTrack>(string? asking = null)` | which member of the enum `TTrack` applies | `ChoiceDecision<TTrack>` |
| `Score<TLevel>(string? asking = null)` | where the state falls on the levels of `TLevel`, lowest value first, read as signed numbers | `ScoreDecision<TLevel>` |
| `YesNo<TQuestion>(string? asking = null, string? yes = null, string? no = null)` | how likely the answer to `TQuestion` is yes | `YesNoDecision<TQuestion>` |
| `DecidedBy<TDecider>()` | asks `TDecider` instead of the registered `IDecider` | |
| `Shadow<TDecider>()` | also asks `TDecider` every question the live decider is asked, records whether it agrees, never acts on it | |
| `WaitForShadows(TimeSpan wait)` | how long to wait for the shadows once the live answer is in, before cancelling them; defaults to five seconds | |

`asking` defaults to the `[Asks]` attribute on the type, and `yes` and `no` to the attribute's
`Yes` and `No`. Each question may be asked once per `Decide`. Each is keyed by its type's name without
the namespace, or by the `Key` set on `[Asks]`, as `QuestionKey.For<T>()` returns it; see
[Question keys](/docs/core/decisions#question-keys).

### Decisions

| Type | Properties |
|---|---|
| `ChoiceDecision<TTrack>` | `Choice`, `Confidence`, `Probabilities` (per member, when given), `Model` |
| `ScoreDecision<TLevel>` | `Score` (0 to the top level), `Nearest` (the level it rounds to, halves up), `Confidence`, `Probabilities`, `Model` |
| `YesNoDecision<TQuestion>` | `Probability` of yes, `Model` |

## Example

```csharp
protected override Task<Either<Exception, ModerationOutcome>> Junctions() =>
    Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Score<Severity>())
        .Gate<ContainsThreat>(gate => gate
            .Yes(t => t.Chain<TakeDownPost>(), atLeast: 0.7)
            .No(t => t.Switch<Verdict>(...), below: 0.3)
            .Unsure(t => t.Chain<PageTrustAndSafety>()))
        .Resolve();
```

## Behavior

1. Skipped if the train is already on the left track; no decider is asked. A cancelled run stops here too.
2. Takes `TState` from Memory, then the decider (`IDecider`, or the `DecidedBy` type) from Memory or the container.
3. For each question, awaits the registered `IDecisionReplay` for an earlier run's answer and the fingerprint it was recorded with. A replayed answer whose fingerprint differs from this asking, or that no longer fits its question, is set aside, with the reason, and the question is asked afresh. Questions it answers are not sent, to the decider or to the shadows.
4. Asks the decider the rest in one `DecisionRequest`. The shadows are asked the same questions at the same time, each on a thread of its own, and each shadow from the container in a DI scope of its own. The live decider is handed the state in Memory itself; each shadow is handed its own copy, written to JSON once and read back for each.
5. Once the live answer is in, waits at most `WaitForShadows` for the shadows, then cancels them; one that has not answered is recorded as not having answered.
6. Checks every answer against its question and puts the typed decision in Memory.
7. Awaits the registered `IDecisionObserver` for each decision, with its `Occurrence`, its `Fingerprint`, any shadows' answers and any `ReplayRefused` reason. An observer that is `Required` and fails, or that cannot be resolved, fails the step.

The run fails, naming the step and classified `Transient`, when an answer does not fit its
question (an option that is not a member, a confidence or probability outside 0 to 1, a score off
the scale, the wrong kind of answer) or a question goes unanswered: a model asked again usually
answers properly. Every bad answer is named in the failure, and each is reported to the observer's `Refused` first. A declaration the startup check would refuse fails it `Permanent`. A decider that throws fails
the run with its own exception and failure class. A shadow that fails or is slow is recorded,
not thrown. A shadow that neither Memory nor the container supplies fails the run, `Permanent`, as
a missing live decider does.

## Remarks

- The startup check records one step per question, verifies the state and every decider and
  shadow are supplied, and counts each decision as available to the steps after it. It refuses two
  types asked about under one key, shadows on a state type JSON cannot copy, and whatever a decider
  that implements `IVetsQuestions` says it cannot answer.
- A decider handed to `AddServices` is passed as an interface; for `DecidedBy` or `Shadow` from
  Memory, declare an interface of your own that extends `IDecider`.
