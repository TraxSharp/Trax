---
title: Decisions
description: "Why a chain declares every track it can take and lets a decider, from a typed decision model to a rule table, choose one per run."
type: explanation
area: trains
nav_order: 6
source:
  - Trax.Core/src/Trax.Core/Decisions
---

# Decisions

A train can take different junctions on different runs, along tracks its chain declares. A
`Switch`, `Gate` or `Scale` names every track it can send the train down; at run time a
**decider** answers a typed question about a value in Memory, and the train takes the matching
track. Every track is part of the declaration, so the host's
[startup check](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic)
verifies all of them before the first run.

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Decisions/SupportDesk.cs
public class TriageTicket(IDecider decider) : Train<SupportRequest, Resolution>
{
    protected override Task<Either<Exception, Resolution>> Junctions() =>
        AddServices(decider)
            .Chain<ParseTicket>()
            .Switch<Ticket, TicketTrack>(tracks =>
                tracks
                    .When(
                        TicketTrack.Refund,
                        t => t.Chain<IssueRefund>().Chain<NotifyCustomer>(),
                        requireConfidence: 0.7
                    )
                    .When(TicketTrack.Escalate, t => t.Chain<OpenIncident>())
                    .When(TicketTrack.SelfServe, t => t.Chain<SendHelpArticle>())
                    .RequireConfidence(0.5)
                    .Otherwise(t => t.Chain<QueueForHuman>())
            )
            .Chain<CloseTicket>()
            .Resolve();
}
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
name, so the words go on the type, once, with `[Asks]` and `[Description]`:

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Decisions/SupportDesk.cs
[Asks("Which team should handle this support ticket?")]
public enum TicketTrack
{
    [System.ComponentModel.Description("The customer wants their money back for an order.")]
    Refund,

    [System.ComponentModel.Description(
        "An outage, a security problem or a legal threat. Page whoever is on call."
    )]
    Escalate,

    [System.ComponentModel.Description("A how-to question the help centre already answers.")]
    SelfServe,
}
```

A yes or no question is a marker type, and says what each answer means:

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Decisions/Moderation.cs
[Asks(
    "Does this post threaten violence against a person?",
    Yes = "It threatens, or incites, harm to someone.",
    No = "It does not threaten anyone, even if it is rude."
)]
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
[fingerprint](#observing-and-replaying). It is not hidden from a model, so a type is best not
named anything you would not show one.

Moving the type to another namespace leaves the key as it was. Renaming it, or a type it is nested
in, changes the key, so the next re-queue or retry asks that question afresh rather than replaying an
answer recorded under the old one. `[Asks(..., Key = "...")]` sets the key explicitly, to keep the
old one across a rename or to give a type a key of its own:

```csharp fragment
[Asks("Which team should handle this support ticket?", Key = "TicketTrack")]
public enum SupportRoute { ... }
```

An explicit key is 1 to 100 characters (`QuestionKey.MaxLength`), each an ASCII letter or digit,
`_`, `-` or `.`; `QuestionKey.IsValid` says whether a string is one. Two different types that one
train asks about under the same key (in one `Decide`, across steps, or inside a track) are refused
by the startup check, and at run time for a host that skips it, because their answers could not be
told apart.

## Asking inline, or asking first

A routing step can ask its own question, as the `Switch<Ticket, TicketTrack>` above does. Or a
[Decide](/docs/sdk-reference/train-methods/decide) step can ask several questions about one state
in a single call, and later steps route on the answers without asking again. That is one round
trip to a model instead of three:

```csharp from=Trax.Core/tests/Trax.Core.Tests.Integration/Examples/Decisions/Moderation.cs
public class ModeratePost(IDecider decider) : Train<Post, ModerationOutcome>
{
    protected override Task<Either<Exception, ModerationOutcome>> Junctions() =>
        AddServices(decider)
            .Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Score<Severity>())
            .Gate<ContainsThreat>(gate =>
                gate.Yes(t => t.Chain<TakeDownPost>(), atLeast: 0.7)
                    .No(t => t.Switch<Verdict>(ModerateByVerdict), below: 0.3)
                    .Unsure(t => t.Chain<PageTrustAndSafety>())
            )
            .Resolve();

    private static Tracks<Post, ModerationOutcome, Verdict> ModerateByVerdict(
        Tracks<Post, ModerationOutcome, Verdict> tracks
    ) =>
        tracks
            .When(Verdict.Allow, t => t.Chain<PublishPost>())
            .When(Verdict.Remove, t => t.Chain<TakeDownPost>())
            .When(
                Verdict.Review,
                t =>
                    t.Scale<Severity>(scale =>
                        scale
                            .AtLeast(Severity.Low, s => s.Chain<QueueForModerator>())
                            .AtLeast(Severity.High, s => s.Chain<PageTrustAndSafety>())
                    )
            );
}
```

Routing steps nest: a track is a chain like any other, so it can ask and route again. Each track is
written on the parameter it is handed (`t => t.Chain<X>()`), not on the train's own chain methods.

## When the answer is not followed

Three things decide whether the train takes the track the decider chose.

| Situation | What happens |
|---|---|
| The choice's confidence is below its track's `requireConfidence`, or below the switch's `RequireConfidence` when the track sets none | The `Otherwise` track runs. Without one, the run fails. |
| The decider chose a member the switch has no track for | The `Otherwise` track runs. Without one, the run fails. |
| A gate's probability falls between its No and Yes bars | The `Unsure` track runs. Without one, the run fails. |

The bars are per track because the cost of a wrong answer is. The track with the gravest
consequence gets the highest bar and the safest gets the lowest, often none: in underwriting,
`Approve` and `Decline` might need 0.95 while `ManualReview` accepts anything. A switch that
declares no `Otherwise` refuses to guess, which is what a train whose outcomes all have
consequences wants.

A choice's confidence depends on how many options it was offered, so adding a track moves every
threshold on that switch. The thresholds need checking against labelled cases again after a
switch's tracks, its question's words, or the model's version change.

The track taken goes in Memory as `TrackTaken<TKey>`: its `Track`, and a `FallbackReason` when the
decision was not followed (`TookFallback` is true then), so a later junction can record or act on
it.

## When the run fails instead

An answer Trax cannot trust is never acted on. The run fails and the failure names the step
(`Switch<Ticket, TicketTrack>`). How it is
[classified](/docs/effect/failure-classification) depends on whose mistake it is.

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
different one for a single step. [Write a decider](/docs/core/write-a-decider) shows the types and
how to register one.

The live decider is handed the object in the train's Memory as `DecisionRequest.State`, not a
copy, and reads it without changing it. Each shadow is handed a copy of its own (see
[Comparing a new decider without trusting it](#comparing-a-new-decider-without-trusting-it)).

A decider that can tell from the declaration alone that it cannot answer a question (too many
options for its model, a question with no words, a state type it cannot send) implements
`IVetsQuestions`, and the chain is refused when it is read, so the host fails at startup rather
than on the first run that asks.

| Decider | Package | For |
|---|---|---|
| `SystemOneDecider`, from `AddNimbleDecider` | Trax.Effect.Decisions.SystemOne | Nimble, Bespoke Labs' open-weights decision model, on a Nimble server you run. The default choice. See [Nimble](/docs/effect/decisions#nimble). |
| `SystemOneDecider`, from `AddSystemOneDecider` | Trax.Effect.Decisions.SystemOne | Another model behind a server that accepts the System One request format, such as Jev. |
| `RuleDecider` | Trax.Core | Policy rather than judgement, written as code. Always certain. |
| `CascadingDecider` | Trax.Core | A fast decider first, and a slower one only for the questions the first was unsure of. |
| `ScriptedDecider` | Trax.Core | Answers written in advance, for tests and for running locally before a model is wired up. |

### Escalating what the fast decider is unsure of

A small decision model is fast and cheap, and usually sure. `CascadingDecider` sends every
question to it first and asks a second, slower decider only the questions the first was unsure of,
answered badly, or left unanswered, so the larger model is paid for on the hard cases only. If the
first decider throws, every question goes to the second, since the first tier being down is what
the second is for. The switch's own bars and fallback tracks still apply to whatever the cascade
returns, which is how a person becomes the third tier.
[Write a decider](/docs/core/write-a-decider#escalate-to-a-second-decider) has its options.

### Comparing a new decider without trusting it

A shadow is a second decider asked every question the live one is asked, alongside it. Its
answers are recorded with whether each agreed, and never acted on; a shadow that fails, disagrees
or is slow changes nothing. That is how a host moves from a rule table to a model, or from one
model version to the next, on real traffic before switching over.

A shadow from the container runs in a DI scope of its own, and every shadow gets its own copy of
the state, so it shares nothing with the run it watches: not a scoped `DbContext`, and not the
object the junctions after the decision read. [Try a decider in shadow](/docs/core/try-a-decider-in-shadow)
shows how to declare one and what agreement means.

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
`IDecisionReplay` for an earlier answer before it asks a decider. Both are optional, found in
Memory and then the container, and Trax.Core implements neither. Trax.Effect implements both, to
record decisions against the run and to make a re-queued or retried run take the tracks the
original took; see [Decision Recording and Models](/docs/effect/decisions).

An observer is best effort by default, because recording a decision must never change it: what it
throws is logged and ignored. An observer whose record the host depends on, such as one a later
replay reads, says so, and then a failure to record fails the step before the train acts on the
decision.

A replayed answer is only as good as the asking and the state it was given for. Each asking has a
fingerprint (the question as declared, never the state's value), and a replayed answer whose
fingerprint differs, or that no longer fits the question, is not acted on: the decider is asked
afresh. [Decision replay and state hash rules](/docs/core/decision-replay) lists the interfaces,
the records and every check.

### A replay matches the state

The fingerprint says an answer was given to the same asking; the state hash says it was given
about the same state. A repeated run runs every step again and reads the state a question is about
afresh, so a recorded answer is replayed only into a state that hashes exactly as the state it was
given about did. An answer is a decision about one state, and a repeated run is usually
unattended, so when the state has changed it asks rather than replays.

The hash covers every value an in-process decider could read in the state, and nothing it reads
from elsewhere: a customer looked up by an id the state holds is not in it, so a change there is
noticed only when the state carries the value itself. A state whose fields differ from run to run
although nothing that matters changed (a timestamp, a counter, a generated id) is asked afresh on
every repeat. The check is made in Trax.Core, so every replay implementation inherits it and only
stores and returns the hash. `Trax.Core/docs/adr/0004` records why.

### Keying the state hash

The hash covers every value in the state, including members a host masks or withholds elsewhere,
and a journal stores it beside the answer. A host should register a `StateHashKey`, so the hash is
an HMAC that only a holder of the key can compute. Without one the hash is unkeyed, and replay
still works. Every process that may repeat a run must use the same key; the rules are in
[Decision replay and state hash rules](/docs/core/decision-replay#statehashkey).

## Testing

A train's decisions are tested with `ScriptedDecider`, which answers what the test says and keeps
every request it was asked. See [Test a train's decisions](/docs/core/test-train-decisions).

## SDK Reference

> [Decide](/docs/sdk-reference/train-methods/decide) | [Switch](/docs/sdk-reference/train-methods/switch) | [Gate](/docs/sdk-reference/train-methods/gate) | [Scale](/docs/sdk-reference/train-methods/scale) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider)
