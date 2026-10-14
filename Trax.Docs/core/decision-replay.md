---
title: Decision replay and state hash rules
description: "IDecisionObserver, IDecisionReplay and their records, the question fingerprint, the checks a replayed answer passes, and the state hash and StateHashKey."
type: reference
area: trains
nav_order: 5
source:
  - Trax.Core/src/Trax.Core/Decisions/IDecisionObserver.cs
  - Trax.Core/src/Trax.Core/Decisions/QuestionFingerprint.cs
  - Trax.Core/src/Trax.Core/Decisions/StateDigest.cs
  - Trax.Core/src/Trax.Core/Decisions/StateHashKey.cs
---

# Decision replay and state hash rules

Trax.Core reports every answered question and every routing to an `IDecisionObserver`, and asks an
`IDecisionReplay` for an earlier answer before it asks a decider. Both are in `Trax.Core.Decisions`,
both are optional, and both are found in the train's Memory, then the container.
[Decisions](/docs/core/decisions#observing-and-replaying) explains why replay works this way;
Trax.Effect's implementation is in [Decision Recording and Models](/docs/effect/decisions).

## IDecisionObserver

```csharp fragment
public interface IDecisionObserver
{
    bool Required => false;
    Task Decided(DecisionMade decision, CancellationToken cancellationToken);
    Task Routed(TrackRouted routing, CancellationToken cancellationToken);
    Task Refused(DecisionRefused refusal, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

| Member | Called |
|---|---|
| `Decided` | Once per answered question, on the train's path, after the answer is checked and before any track is taken on it. |
| `Routed` | Once per routing step, with the track taken. |
| `Refused` | Once for each question whose live answer the step will not act on (missing, or not fitting the question), before the step fails. A cascade that ended with an answer that fits is not a refusal; a shadow's bad answer goes in its `ShadowAnswer.Error` instead. A refusal is never replayed. |
| `Required` | `false`: what the observer throws is logged and ignored. `true`: a failure to record fails the step before the train acts on the decision, classified as the exception says, or `Transient` when it says nothing. |

An observer that cannot be resolved at all fails the step either way. An observer that cannot
record a refusal is logged, and never replaces the refusal as the reason the step failed, even
when it is `Required`.

### DecisionMade

| Member | Description |
|---|---|
| `Train`, `RunId` | The train and the run that asked. |
| `Question` | The question as it was asked. |
| `Occurrence` | How many times this run asked that question before, from 0. |
| `Fingerprint` | The asking's [fingerprint](#fingerprint). |
| `Answer` | The answer acted on. |
| `Decider` | The decider's type, or null for a replayed answer. |
| `Replayed` | Whether the answer was replayed rather than asked. |
| `Shadows` | Each shadow's `ShadowAnswer`: `Decider`, `Answer` (null when it gave none), `Agrees`, `Error`. |
| `ReplayRefused` | Which [replay check](#replay-checks) refused a recorded answer, or null. |
| `StateHash` | The [state hash](#state-hash), when one was taken. |
| `StateType` | The type of the state the hash was taken of: its runtime type, or the type the step declared when the state was null. |
| `QuestionType` | The type the question was asked about: the enum a choice or score is between, or the marker type of a yes or no question. Null only on a record built outside a run. |

`StateType` and `QuestionType` let a host treat a decision by what it was about, matching with
inheritance, where a key would match only one name.

### DecisionRefused

`Train`, `RunId`, `Question`, `Occurrence`, `Fingerprint`, `Answer` (null when there was none),
`Decider`, `Reason` and `QuestionType`.

### TrackRouted

`Train`, `RunId`, `On` (the enum or marker type routed on), `Track` (the track's name), and
`FallbackReason`, set when the decision was not followed and a fallback track ran.

## IDecisionReplay

```csharp fragment
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

`Replay` is asked on the run's path, once per question, by the same coordinates `DecisionMade`
carries. It returns null to ask the decider. A host that replays stores each `DecisionMade.Answer`
with its `Fingerprint` and `StateHash` and hands them back exactly as stored. Whatever `Replay`
throws fails the step, classified as the exception says, because a replay that cannot be read
cannot be told from a run with nothing to replay. A question whose answer is replayed is not put
to the shadows.

## Fingerprint

A SHA-256 over one asking of a question as the chain declares it: the step, the state's type, the
kind of question, its key, its words, and every option or level with its description (or what yes
and no mean). It never covers the state's value.

## Replay checks

A replayed answer is checked before it is acted on. The decider is asked afresh when:

| Check | Fails when |
|---|---|
| Fingerprint | It differs from the question as asked now: the question was reworded, its options, levels or descriptions changed, or the chain changed so that another step now asks it first. |
| Fit | The answer no longer fits the question: an option renamed or removed from the enum, a scale with fewer levels, another kind of question. |
| State hash | Its `StateHash` differs from the hash of the state the question is asked about now, or is null. |

`DecisionMade.ReplayRefused` says which check refused the answer, and never quotes the recorded
answer. A recorded choice of a member the switch has no track for is replayed like any other and
takes the `Otherwise` track again.

| The replayed answer's state hash | What happens |
|---|---|
| Equal to the hash of the state asked about now | Replayed |
| Different, including one taken under another key or without a key | Asked afresh, with the reason in `ReplayRefused` |
| Null (an answer recorded before states were hashed, or about a state that could not be hashed) | Asked afresh, with the reason in `ReplayRefused` |
| The state asked about now cannot be hashed (below) | Asked afresh, with the reason in `ReplayRefused`. Hashing never fails the run. |

When the state hash key is the cause, `ReplayRefused` names it: an answer recorded under a key
while this run hashes without one, or the reverse, says so; a keyed hash that differs says the
state changed "or the state hash key has changed since"; and a key that cannot be resolved is
reported as that, not as a change in the state.

## State hash

Taken only when needed: to check a replayed answer that passed its fingerprint and fit checks, or
before the decider is asked when a decision observer is registered, so the hash an observer
records is of the state the decider was given.

| Form | When |
|---|---|
| `k1:` and the lowercase hex of an HMAC-SHA256 | A [`StateHashKey`](#statehashkey) is registered |
| `s1:` and the lowercase hex of a SHA-256 | No key is registered |

A keyed and an unkeyed hash never match.

### What it covers

- Every instance field of the state's runtime type, public or not, and every value they hold,
  recursively: auto-properties, tuple items, record members, and the members of a derived type held
  where a base type or an interface is declared.
- Each value's runtime type, named with its assembly, and its fields base type first and by name.
- Every element of an array, inline array or fixed buffer, and each array dimension's length and
  lower bound.
- A framework collection as its elements and its comparers. A wrapper (a read-only wrapper, a
  `Collection<T>` and so a keyed or observable collection, a view of a dictionary's keys) as what
  it wraps, with its own comparers, so a key view is written as its whole dictionary and a list of
  the state's own type by its fields.
- A set, dictionary or bag with its elements sorted, so equal contents hash alike whatever order
  they were added in.
- An enum with its underlying type and its members' names and values; a local `DateTime` with its
  daylight-saving flag.

It covers the state's value and nothing else: what a decider reads from elsewhere is not in it.
Only the hash leaves the run, never the state.

### States with no hash

`StateHash` is null, and the state never replays, when the state:

- holds a reference cycle
- is nested deeper than 64 levels, or its encoding passes 1,000,000 values or 16 MiB
- holds a delegate (an ORM's lazy-loading proxy, for instance), a pointer or native handle
- holds a `Type`, a `MemberInfo` or an `Assembly`
- holds a stream, a wait handle, a task or a thread
- holds a non-generic `Hashtable` or `SortedList`, a `BlockingCollection<T>`, a wrapper
  `ArrayList` makes (`ArrayList.Adapter`, `ReadOnly`, `Synchronized`...), or a collection that
  enumerates by its own code
- holds a `CultureInfo` other than the framework's shared instance (`CultureInfo.GetCultureInfo(name)`
  or `InvariantCulture`); `CurrentCulture` and `new CultureInfo(...)` give no hash
- has a field that throws when read, or a collection that throws when enumerated

It is also null when the container's `StateHashKey` cannot be resolved.

## StateHashKey

```csharp compile
using Trax.Core.Decisions;

var stateHashKeyBytes = Convert.FromBase64String(configuration["StateHashKey"]!);

services.AddSingleton(new StateHashKey(stateHashKeyBytes));   // at least 32 bytes
```

| Member | Description |
|---|---|
| `StateHashKey(byte[] key)` | Copies `key`. Throws `ArgumentException` when it is null or shorter than 32 bytes. Drawn from a cryptographic random source and kept as the host keeps its other secrets. |

Trax.Core reads the key from the train's container. Hashes are compared exactly, so every process
that may repeat a run uses the same key, and adding or changing the key means each answer recorded
before it is asked afresh once. Trax.Effect's
[`AddDecisionRecording`](/docs/sdk-reference/configuration/add-decision-recording) can supply the
key from its options or from configuration.

## SDK Reference

> [Decide](/docs/sdk-reference/train-methods/decide) | [Switch](/docs/sdk-reference/train-methods/switch) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording)
