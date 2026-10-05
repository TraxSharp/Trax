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
in, changes the key, so the next re-queue or retry asks that question afresh rather than replaying an
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

### Writing a decider

Every type below is in `Trax.Core.Decisions` (package `Trax.Core`, which Trax.Effect brings in):

```csharp
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

Return one answer per question, keyed by `Question.Key`, of the kind the question asks for: a
`ChoiceAnswer` naming one of the options' `Criterion.Name` (the enum member's name), a
`ScoreAnswer` from 0 (the first level) to one less than the number of levels, or a `YesNoAnswer`
with the probability of yes. Set `Model` to the model and version that answered, or leave it null
for a decider that is not a model. `State` is the object in Memory, typed as `object`: match on its
type. An answer that does not fit, or a question left unanswered, fails the run (see
[When the run fails instead](#when-the-run-fails-instead)); throwing fails it with your exception.

```csharp
using Trax.Core.Decisions;

public sealed class TicketRules : IDecider
{
    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        var ticket = (Ticket)request.State;
        var answers = request.Questions.ToDictionary(
            q => q.Key,
            q => q switch
            {
                ChoiceQuestion => (Answer)new ChoiceAnswer(ticket.MentionsRefund ? "Refund" : "SelfServe", 0.9),
                YesNoQuestion => new YesNoAnswer(ticket.IsAngry ? 0.8 : 0.1),
                _ => throw new InvalidOperationException($"Cannot answer {q.Key}."),
            });
        return Task.FromResult(new DecisionResult(answers));
    }
}

services.AddSingleton<IDecider, TicketRules>();   // any lifetime; one serves every train
```

A decider whose runs should be replayable answers from the state alone. Replay checks the hash of
the state and nothing else, so an answer that depended on something the decider looked up
elsewhere is replayed even after that changed.

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
answer (null when there was none), the decider, the reason and the `QuestionType`. A cascade that ended with an answer
that fits is not a refusal, and a shadow's bad answer is reported in its `ShadowAnswer.Error`
instead. A refusal is never replayed. An observer that cannot record one is logged, and never
replaces the refusal as the reason the step failed, even when it is `Required`.

By default an observer is best effort: what it throws is logged and ignored, because recording a
decision must never change it. An observer whose record the host depends on, such as one a later
replay reads, returns `true` from `Required`, and then a failure to record fails the step before
the train acts on the decision, classified as the exception says or `Transient` when it says
nothing. An observer that cannot be resolved at all fails the step either way.

`DecisionMade` carries the question, the answer, the decider, whether it was replayed, the
shadows' answers, `Occurrence` (how many times the run asked that question before, from 0),
`Fingerprint`, `StateHash`, `StateType` and `QuestionType`. The replay is asked by the same
coordinates, on the run's path, once per question:

```csharp
public interface IDecisionReplay
{
    Task<RecordedAnswer?> Replay(
        string train, string runId, string key, int occurrence, CancellationToken cancellationToken);
}

public sealed record RecordedAnswer(Answer Answer, string Fingerprint)
{
    public string? StateHash { get; init; }
}
```

It returns null to ask the decider. A host that replays stores each `DecisionMade.Answer` with its
`Fingerprint` and `StateHash` and hands them back exactly as stored. Whatever `Replay` throws fails the step,
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
- it was given about a different state: its `StateHash` differs from the hash of the state the
  question is asked about now, or is null

In each case the decider is asked afresh, and `DecisionMade.ReplayRefused` says which check
refused the answer, naming the state hash key when the key is the cause (see the table below). A recorded
choice of a member the switch has no track for is replayed like any other and takes the
`Otherwise` track again, as it did the first time.

### A replay matches the state

The fingerprint says an answer was given to the same asking; the state hash says it was given
about the same state. A repeated run runs every step again and reads the state a question is about
afresh, so a recorded answer is replayed only into a state that hashes exactly as the state it was
given about did.

`StateHash` is taken only when it is needed: to check a replayed answer that passed its fingerprint
and fit checks, or before the decider is asked when a decision observer is registered, so the hash
an observer records is of the state the decider was given. Under a [`StateHashKey`](#keying-the-state-hash)
it is `k1:` and the lowercase hex of an HMAC-SHA256 under that key; without one it is `s1:` and
the lowercase hex of a SHA-256. A keyed and an unkeyed hash never match. It covers every instance
field of the state's runtime type, public or not, and every value they hold, recursively:
auto-properties, tuple items, record members, and the members of a derived type held where a base
type or an interface is declared all count, because an in-process decider can read all of them.
Each value is written with its runtime type, named with its assembly, and its fields base type
first and by name. Every element of an array, inline array or fixed buffer counts, and so do each
array dimension's length and lower bound. A framework collection is written as its elements and
its comparers. A wrapper (a read-only wrapper, a `Collection<T>` and so a keyed or observable
collection, a view of a dictionary's keys) is written as what it wraps, with its own comparers, so a
key view is written as its whole dictionary and a list of the state's own type is written by its
fields. An enum is written with its underlying type and its members' names and values, and a local
`DateTime` with its daylight-saving flag. A set, dictionary or bag is written with
its elements sorted, so equal contents hash alike whatever order they were added in. The check is
made in Trax.Core, so every replay implementation inherits it and only stores and returns the hash.

The hash covers the state's value and nothing else. What a decider reads from elsewhere (a
customer it looks up by an id the state holds, say) is not in it, so a change there is noticed only
when the state carries the value itself.

| The replayed answer's state hash | What happens |
|---|---|
| Equal to the hash of the state asked about now | Replayed |
| Different, including one taken under another key or without a key | Asked afresh, with the reason in `ReplayRefused` |
| Null (an answer recorded before states were hashed, or about a state that could not be hashed) | Asked afresh, with the reason in `ReplayRefused` |
| The state asked about now cannot be hashed (below) | Asked afresh, with the reason in `ReplayRefused`. Hashing never fails the run. |

`ReplayRefused` says which check refused the answer and never quotes the recorded answer. When the
state hash key is the cause, it names the key: an answer recorded under a key while this run hashes
without one, or the reverse, says so; a keyed hash that differs says the state changed "or the state
hash key has changed since"; and a key that cannot be resolved is reported as that, not as a change
in the state.

A state has no hash, and so never replays, when it cannot be read the same way every time:

- it holds a reference cycle
- it is nested deeper than 64 levels, or its encoding passes 1,000,000 values or 16 MiB
- it holds a delegate (an ORM's lazy-loading proxy, for instance), a pointer or native handle
- it holds a `Type`, a `MemberInfo` or an `Assembly`
- it holds a stream, a wait handle, a task or a thread
- it holds a non-generic `Hashtable` or `SortedList`, a `BlockingCollection<T>`, a wrapper
  `ArrayList` makes (`ArrayList.Adapter`, `ReadOnly`, `Synchronized`...), or a collection that
  enumerates by its own code
- it holds a `CultureInfo` other than the framework's shared instance (`CultureInfo.GetCultureInfo(name)`
  or `InvariantCulture`); `CurrentCulture` and `new CultureInfo(...)` give no hash
- reading one of its fields throws
- enumerating one of its collections throws

`StateHash` is null in those cases, and when the container's `StateHashKey` cannot be resolved.

A state whose fields differ from run to run although nothing that matters changed (a timestamp, a
cache, a counter, a generated id) is asked afresh on every repeat. Keep such values out of the
state a question is asked about when replay matters. Only the hash leaves the run, never the
state. `Trax.Core/docs/adr/0004` records why.

`StateType` is the type of the state the hash was taken of: its runtime type, or the type the step
declared it as when the state was null. Like `QuestionType`, it lets a host treat a decision by
what it was about, matching with inheritance.

### Keying the state hash

The hash covers every value in the state, including members a host masks or withholds elsewhere,
and a journal stores it beside the answer. Register a `StateHashKey` so it is keyed, and only a
holder of the key can compute it:

```csharp
services.AddSingleton(new StateHashKey(stateHashKeyBytes));   // at least 32 bytes
```

| Member | Description |
|---|---|
| `StateHashKey(byte[] key)` | Copies `key`. Throws `ArgumentException` when it is null or shorter than 32 bytes. Draw it from a cryptographic random source and keep it as the host keeps its other secrets. |

Trax.Core reads the key from the train's container. Every process that may repeat a run must use
the same key: hashes are compared exactly, so adding or changing the key means each answer
recorded before it is asked afresh once. Trax.Effect's
[`AddDecisionRecording`](/docs/sdk-reference/configuration/add-decision-recording) can supply the
key from its options or from configuration.

`QuestionType` is the type the question was asked about (the enum a choice or score is between, or
the marker type of a yes or no question), so a host can treat questions by type, with inheritance,
where a key would match only one name. It is null only on a record built outside a run.

Trax.Effect implements both, to record decisions against the run and to make a re-queued or
retried run take the tracks the original took. See [Decision Recording and Models](/docs/effect/decisions).

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
