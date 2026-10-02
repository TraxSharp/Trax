---
layout: default
title: Decisions
description: How a chain declares several tracks and lets a decider, from a typed decision model to a rule table, choose one per run, with every track verified at startup.
parent: Core
nav_order: 6
---

# Decisions

A train can take different junctions on different runs, along tracks its chain declares. A
`Switch`, `Gate` or `Scale` names every track it can send the train down; at run time a
**decider** answers a typed question about a value in Memory, and the train takes the matching
track. Every track is part of the declaration, so the host's
[startup check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
verifies all of them before the first run.

```csharp
protected override Task<Either<Exception, Resolution>> Junctions() =>
    Chain<ParseTicket>()
        .Switch<Ticket, TicketTrack>(tracks => tracks
            .When(TicketTrack.Refund, t => t.Chain<IssueRefund>().Chain<NotifyCustomer>(),
                  requireConfidence: 0.7)
            .When(TicketTrack.Escalate, t => t.Chain<OpenIncident>())
            .When(TicketTrack.SelfServe, t => t.Chain<SendHelpArticle>())
            .RequireConfidence(0.5)
            .Otherwise(t => t.Chain<QueueForHuman>()))
        .Chain<CloseTicket>()
        .Resolve();
```

The decider can be a typed decision model (one state and typed questions in, calibrated
probabilities out, no generated text), a rule table, a larger language model, or a test double.
The train does not know which. `Trax.Docs/adr/0040` records why a chain branches this way and no
other.

## Questions and answers

Every decision is one of three kinds of question, each with its own routing step.

| Question | Declared with | Answer | Routed by | Memory holds |
|---|---|---|---|---|
| Which of these options? | `Choice<TTrack>()`, an enum | the chosen member, a confidence, each member's probability | [Switch](/docs/sdk-reference/train-methods/switch) | `ChoiceDecision<TTrack>` |
| Where on this ordered scale? | `Score<TLevel>()`, an enum whose values, read as signed numbers, run lowest to highest | a position from 0 to the top level, a confidence, each level's probability | [Scale](/docs/sdk-reference/train-methods/scale) | `ScoreDecision<TLevel>` |
| How likely is yes? | `YesNo<TQuestion>()`, a marker type | the probability of yes | [Gate](/docs/sdk-reference/train-methods/gate) | `YesNoDecision<TQuestion>` |

A model is meant to judge by the question's words and each option's description, not the type's
name. The words go on the type, once:

```csharp
[Asks("Which team should handle this support ticket?")]
public enum TicketTrack
{
    [Description("The customer wants their money back for an order.")]
    Refund,

    [Description("An outage, a security problem or a legal threat. Page whoever is on call.")]
    Escalate,

    [Description("A how-to question the help centre already answers.")]
    SelfServe,
}

[Asks("Does this post threaten violence against a person?",
      Yes = "It threatens, or incites, harm to someone.",
      No = "It does not threaten anyone, even if it is rude.")]
public sealed class ContainsThreat;
```

An `asking:` argument where the decision is declared overrides `[Asks]`. A decision with no
question at all is refused when the chain is read.

### Question keys

Each question is keyed by the name of the type it is about, as `QuestionKey.For<T>()` returns
it: the type's name after the name of every type it is nested in, joined by dots, with generic
arguments in angle brackets, and no namespace (`TicketTrack`, `Queue.Lane` for an enum nested in a
class, `Flag<Refund>` for a generic marker). A decider finds each answer's question by it, a model
adapter may send it as the question's id, as the System One adapter does (Nimble puts it in its
prompt), recording and replay look answers up by it, and it is part of every asking's
[fingerprint](#observing-and-replaying). It is not hidden from a model, so do not name a type
anything you would not show one.

Moving the type to another namespace leaves the key as it was. Renaming it, or a type it is nested
in, changes the key, so the next re-queue asks that question afresh rather than replaying an
answer recorded under the old one. `[Asks(..., Key = "...")]` sets the key explicitly, to keep the
old one across a rename or to give a type a key of its own:

```csharp
[Asks("Which team should handle this support ticket?", Key = "TicketTrack")]
public enum SupportRoute { ... }
```

An explicit key is 1 to 100 characters, each an ASCII letter or digit, `_`, `-` or `.`. Two
different types that one train asks about under the same key (in one `Decide`, across steps, or
inside a track) are refused by the startup check, and at run time for a host that skips it,
because their answers could not be told apart; set `Key` on one of them.

## Asking inline, or asking first

A routing step can ask its own question:

```csharp
.Switch<LoanApplication, Underwriting>(tracks => tracks ...)   // asks, then routes
```

Or a [Decide](/docs/sdk-reference/train-methods/decide) step can ask several questions about one
state in a single call, and later steps route on the answers without asking again. That is one
round trip to a model instead of three:

```csharp
AddServices(decider)
    .Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Score<Severity>())
    .Gate<ContainsThreat>(gate => gate
        .Yes(t => t.Chain<TakeDownPost>(), atLeast: 0.7)
        .No(t => t.Switch<Verdict>(verdict => verdict
            .When(Verdict.Allow, a => a.Chain<PublishPost>())
            .When(Verdict.Remove, r => r.Chain<TakeDownPost>())
            .When(Verdict.Review, r => r.Scale<Severity>(scale => scale
                .AtLeast(Severity.Low, s => s.Chain<QueueForModerator>())
                .AtLeast(Severity.High, s => s.Chain<PageTrustAndSafety>())))),
            below: 0.3)
        .Unsure(t => t.Chain<PageTrustAndSafety>()))
    .Resolve();
```

Routing steps nest: a track is a chain like any other, so it can ask and route again. Write each
track on the parameter it is handed (`t => t.Chain<X>()`), not on the train's own chain methods.

## When the answer is not followed

Three things decide whether the train takes the track the decider chose.

| Situation | What happens |
|---|---|
| The choice's confidence is below its track's `requireConfidence`, or below the switch's `RequireConfidence` when the track sets none | The `Otherwise` track runs. Without one, the run fails. |
| The decider chose a member the switch has no track for | The `Otherwise` track runs. Without one, the run fails. |
| A gate's probability falls between its No and Yes bars | The `Unsure` track runs. Without one, the run fails. |

Set the bars per track. The track with the gravest consequence gets the highest bar and the
safest gets the lowest, often none: in underwriting, `Approve` and `Decline` might need 0.95
while `ManualReview` accepts anything. A switch that declares no `Otherwise` refuses to guess,
which is what a train whose outcomes all have consequences wants.

A choice's confidence depends on how many options it was offered, so adding a track moves every
threshold on that switch. Re-check them against labelled cases after changing a switch's tracks,
its question's words, or the model's version.

The track taken goes in Memory as `TrackTaken<TKey>`, with the reason when the decision was not
followed, so a later junction can record or act on it.

## When the run fails instead

An answer Trax cannot trust is never acted on. The run fails and the failure names the step
(`Switch<Ticket, TicketTrack>`). How it is
[classified](/docs/core/trains-and-junctions#classifying-failures) depends on whose mistake it is.

A decider's answer that does not fit is classified `Transient`, because a model asked again
usually answers properly:

- an option that is not a member of the enum, including a member given as a number
- a confidence or probability outside 0 to 1, or not a number
- a score below 0 or above the top level
- the wrong kind of answer, or no answer to a question that was asked

Every bad answer in one `Decide` is named in the failure, not only the first, and each is reported
to the observer's [`Refused`](#observing-and-replaying) before the step fails.

A fault in the declaration, or a decision the declaration gives nowhere to go, is classified
`Permanent`, because running it again gets the same refusal:

- a declaration the startup check refuses, for a host that skips the check
- a decision with no track to take: a choice below its bar, or naming a member with no track, on a
  switch with no `Otherwise`, or a gate's probability in a band with no `Unsure` track

A decider that throws fails the run with its own exception, whatever fallback tracks are
declared: an error is not a decision. The decider's own failure class is kept, so an adapter can
mark a throttled model `Transient`. A cancelled run stops before it decides, before it tells an
observer, and before it enters a track.

## Deciders

`IDecider` takes a `DecisionRequest` (the train's name, the state, the questions) and returns one
answer per question. It is found the way a junction input is: in Memory, then the container. One
registration serves every decision in every train; `Decide(q => q.DecidedBy<TDecider>())` names a
different one for a single step.

The live decider is handed the object in the train's Memory as `DecisionRequest.State`, not a
copy, and reads it without changing it. Each shadow is handed a copy of its own (see
[Comparing a new decider without trusting it](#comparing-a-new-decider-without-trusting-it)).

A decider that can tell from the declaration alone that it cannot answer a question (too many
options for its model, a question with no words, a state type it cannot send) implements
`IVetsQuestions`. When a chain is read, each decider a step names, the live one and each shadow, is
looked up as the run finds it, among the services handed to `AddServices` and then in the
container, and shown the step's questions as `DeclaredQuestions` (the train, the step, the state's
declared type, the questions). Each problem it names refuses the chain, so the startup check
reports it before the host takes traffic. No decider is asked to decide while the chain is read,
and one the container can only build inside a request is not vetted and refuses at run time
instead. `CascadingDecider` vets with each of its tiers.

```csharp
public interface IVetsQuestions
{
    IEnumerable<string> Problems(DeclaredQuestions declared);
}
```

| Decider | Package | For |
|---|---|---|
| `SystemOneDecider`, from `AddNimbleDecider` | Trax.Effect.Decisions.SystemOne | Nimble, Bespoke Labs' open-weights decision model, on a Nimble server you run. The default choice. See [Nimble](/docs/effect/decisions#nimble). |
| `SystemOneDecider`, from `AddSystemOneDecider` | Trax.Effect.Decisions.SystemOne | Another model behind a server that accepts the System One request format, such as Jev. |
| `RuleDecider` | Trax.Core | Policy rather than judgement, written as code. Always certain. |
| `CascadingDecider` | Trax.Core | A fast decider first, and a slower one only for the questions the first was unsure of. |
| `ScriptedDecider` | Trax.Core | Answers written in advance, for tests and for running locally before a model is wired up. |

```csharp
var policy = new RuleDecider().Choice<LoanApplication, Underwriting>(application =>
    application switch
    {
        { CreditScore: >= 740, DebtToIncome: <= 0.36m } => Underwriting.Approve,
        { CreditScore: < 580 } or { DebtToIncome: > 0.5m } => Underwriting.Decline,
        _ => Underwriting.ManualReview,
    });
```

### Escalating what the fast decider is unsure of

```csharp
services.AddSingleton<IDecider>(sp => new CascadingDecider(
    first: sp.GetRequiredService<SystemOneDecider>(),   // Nimble
    then: sp.GetRequiredService<LargeModelDecider>(),
    escalateBelow: 0.8));
```

`LargeModelDecider` stands for an `IDecider` of your own over a chat model; Trax does not ship
one. A choice or score below `escalateBelow`, a yes/no whose probability lies strictly between
`unsureAbove` (0.2) and `unsureBelow` (0.8), a confidence or probability that is not a number, a
question the first decider did not answer, and an answer that does not fit its question (an option
it was not offered, a score off the levels, a confidence or probability outside 0 to 1, another
kind of answer) are asked again of the second. Only those questions are sent. If the first decider throws, every question goes to the second, since the first tier
being down is what the second is for; cancellation passes straight through. Pass an `ILogger` as
`logger` to be told when that happens. The second answer replaces the first; if the second
decider fails, the run fails. The bounds must lie between 0 and 1, with `unsureAbove` not above
`unsureBelow`, or the constructor throws. The switch's own bars and fallback tracks still apply to
whatever the cascade returns, which is how a person becomes the third tier.

### Comparing a new decider without trusting it

```csharp
.Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Shadow<ICandidateDecider>())
```

A shadow is asked every question the live decider is asked, alongside it. Its answers are
recorded with whether each agreed, and never acted on; a shadow that fails, disagrees or is slow
changes nothing. Once the live answer is in, the shadows are waited for at most
`WaitForShadows(TimeSpan)` (five seconds by default), then cancelled and recorded as not having
answered, whether or not they honour the cancellation. Every run can wait that long for a slow
shadow, so keep it short; zero waits only for shadows that have already answered. A question whose
answer is replayed is not put to the shadows at all.

A `Switch`, `Gate` or `Scale` that asks its own question declares shadows on its tracks:

```csharp
.Switch<Ticket, TicketTrack>(tracks => tracks
    .When(TicketTrack.Refund, t => t.Chain<IssueRefund>())
    .Otherwise(t => t.Chain<QueueForHuman>())
    .Shadow<ICandidateDecider>()
    .WaitForShadows(TimeSpan.FromSeconds(2)))
```

Agreement there means the shadow's answer would have sent the train down the same track, by the
step's own bars and bands. On a plain `Decide`, whose routing comes later, it means the same
option, the same nearest level, or the same side of one half for a yes/no. A shadow whose answer
does not fit the question never agrees, and neither does any shadow when the live answer takes no
track (a gate's band with no `Unsure` track, a choice with no track and no `Otherwise`). A routing
step that only routes on an earlier `Decide` asks nothing, so shadows declared on it are refused;
shadow the `Decide`.

Use a shadow to move from a rule table to a model, or from one model version to the next, on real
traffic before switching over. A shadow handed to `AddServices` is passed as an interface, as every
service there is; from the container, a concrete type does. A shadow from the container is built
in a DI scope of its own, disposed when the shadow ends, so it never shares the run's scoped
services (a `DbContext`, say) with the live decider or with the junctions that run after the
decision. A shadow that has not finished a second after it was cancelled has its scope disposed
under it, on a timer the run never waits for, and whatever it then fails with is recorded as its
own failure.

Each shadow is handed its own copy of the state: the state is written to JSON once per asking,
with the web defaults the System One adapter uses, and read back separately for each shadow, so a
shadow that changes its copy changes nothing the run or another shadow sees. A state type JSON
cannot write and read back is refused by the startup check when shadows are declared on it and
the type alone says so; otherwise a shadow whose copy cannot be made is recorded as not having
answered, and the run goes on.

A shadow that neither supplies is a mistake in the host: the startup check reports it,
and a run that reaches it fails, `Permanent`, as it does for a missing live decider.

## What the startup check verifies

Every track is read when the chain is declared, and each is replayed from the Memory at the
routing step. After a routing step the chain can rely only on what **every** track produces:
a junction after a switch that needs a value only one track makes is refused, naming it. A fault
inside a track is reported at the routing step, as `track 'Refund', step 1: ...`.

The check also refuses a routing step with no decision before it, a decider or shadow that
neither Memory nor the container supplies, a shadow named twice or declared on a step that asks
nothing, a negative or unbounded `WaitForShadows`, a track declared twice, a track on a value the
enum does not define (`When((Lane)7, ...)`, or the same in `AtLeast`), a switch with no tracks, a
gate with no Yes or No track or with bars that cross, a scale on an enum with fewer than two
levels, a scale with no track for its lowest level, a `Key` on `[Asks]` that is not a valid key,
two types asked about under one key, shadows on a state type JSON cannot copy, and whatever a
decider that implements `IVetsQuestions` names. Reading the chain asks no decider and runs no
track. A routing step that asks its own question checks its tracks before it asks, so a host
that skips the check still never pays for a decision on a declaration it would refuse.

## Observing and replaying

Core reports every answered question and every routing to an `IDecisionObserver`, and asks an
`IDecisionReplay` for an earlier answer before it asks a decider. Both are optional and found in
Memory, then the container.

The observer is awaited on the train's path, after the answer is checked and before any track is
taken on it:

```csharp
public interface IDecisionObserver
{
    bool Required => false;
    Task Decided(DecisionMade decision, CancellationToken cancellationToken);
    Task Routed(TrackRouted routing, CancellationToken cancellationToken);
    Task Refused(DecisionRefused refusal, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`Refused` is told once for each question whose live answer the step will not act on (missing, or
not fitting the question), before the step fails, with the question, occurrence, fingerprint, the
answer (null when there was none), the decider and the reason. A cascade that ended with an answer
that fits is not a refusal, and a shadow's bad answer is reported in its `ShadowAnswer.Error`
instead. A refusal is never replayed. An observer that cannot record one is logged, and never
replaces the refusal as the reason the step failed, even when it is `Required`.

By default an observer is best effort: what it throws is logged and ignored, because recording a
decision must never change it. An observer whose record the host depends on, such as one a later
replay reads, returns `true` from `Required`, and then a failure to record fails the step before
the train acts on the decision, classified as the exception says or `Transient` when it says
nothing. An observer that cannot be resolved at all fails the step either way.

`DecisionMade` carries the question, the answer, the decider, whether it was replayed, the
shadows' answers, `Occurrence` (how many times the run asked that question before, from 0) and
`Fingerprint`. The replay is asked by the same coordinates, on the run's path, once per question:

```csharp
public interface IDecisionReplay
{
    Task<RecordedAnswer?> Replay(
        string train, string runId, string key, int occurrence, CancellationToken cancellationToken);
}

public sealed record RecordedAnswer(Answer Answer, string Fingerprint);
```

It returns null to ask the decider. A host that replays stores each `DecisionMade.Answer` with its
`Fingerprint` and hands both back exactly as stored. Whatever `Replay` throws fails the step,
classified as the exception says, because a replay that cannot be read cannot be told from a run
with nothing to replay.

The fingerprint identifies one asking of a question as the chain declares it: a SHA-256 over the
step, the state's type, the kind of question, its key, its words, and every option or level with
its description (or what yes and no mean). It never covers the state's value, which differs from
run to run by design. A replayed answer is checked before it is acted on, and is not acted on when:

- its fingerprint differs from the question as it is asked now: the question was reworded, its
  options, levels or descriptions changed, or the chain changed so that another step now asks it
  first
- it no longer fits the question: an option renamed or removed from the enum, a scale with fewer
  levels, another kind of question

Either way the decider is asked afresh, and `DecisionMade.ReplayRefused` says why. A recorded
choice of a member the switch has no track for is replayed like any other and takes the
`Otherwise` track again, as it did the first time.

Trax.Effect implements both, to record decisions against the run and to make a re-queued run take
the tracks the original took. See [Decision Recording and Models](/docs/effect/decisions).

## Testing a train's decisions

```csharp
var decider = new ScriptedDecider().Choose(Underwriting.Approve, confidence: 0.62);

var result = await new UnderwriteLoan(decider).RunEither(new LoanApplication("a3", 700, 0.4m));

result.IsLeft.Should().BeTrue();          // below Approve's 0.95, and no Otherwise
decider.Requests.Should().ContainSingle(); // what a model would have been shown
```

`ScriptedDecider` answers `Choose`, `Score` and `YesNo` with what the test says, can answer a
question with an answer that does not fit (`Answer(key, ...)`) or fail every request (`Throws`),
and keeps every request it was asked. A question it has no answer for is left unanswered, which
fails the run the way a model that skipped one would.

## SDK Reference

> [Decide](/docs/sdk-reference/train-methods/decide) | [Switch](/docs/sdk-reference/train-methods/switch) | [Gate](/docs/sdk-reference/train-methods/gate) | [Scale](/docs/sdk-reference/train-methods/scale) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider)
