---
layout: default
title: TraxSensitive
description: Reference for TraxSensitive, which masks a member of a train's input or output in stored copies, and withholds a question type's answers from junction events.
parent: Attributes
grand_parent: SDK Reference
nav_order: 3
---

# TraxSensitive

Marks a property, field or record parameter of a train's input or output, or of anything reachable from them, whose value must not appear in the copies Trax keeps. The train runs with the real value; only the written copy is masked.

On the enum or marker type a routing step asks about, it withholds that question's answers from [junction events](/docs/effect/junction-events). See [On a question type](#on-a-question-type).

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(
    AttributeTargets.Property
        | AttributeTargets.Field
        | AttributeTargets.Parameter
        | AttributeTargets.Enum
        | AttributeTargets.Class
        | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxSensitiveAttribute : Attribute
{
    public TraxSensitiveAttribute();
}
```

## What is masked

A marked value is written as `{"_redacted": true}` under the same JSON name in:

| Copy | Written by |
|------|------------|
| The stored input and output of a run | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) |
| A junction's logged output | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) |
| The output handed to lifecycle hooks | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook), the broadcaster, GraphQL subscriptions |

and so everywhere those copies travel: the dashboard, the API's execution detail, logs and subscriptions.

## Rules

- **Opt-in.** Nothing is masked because of its name.
- **A marked member hides its whole value.** An object under it is not walked; a collection is replaced whole.
- **An unmarked member is walked.** A marked property on a nested object, or on each element of a collection, is masked where it sits.
- **Records.** On a positional record, write the attribute on the parameter, with or without `property:`.
- **Inheritance.** A mark on a base property, or on an interface property, applies to the override or implementation.
- **Dictionaries are not masked.** Keys and values have no member to mark.

## What it does not cover

The copy a train is *run* from keeps the real value, because the train needs it: a queued entry's input (`work_queue.input`) and a manifest's properties. Those are JSON strings the mark cannot reach into, so Trax keeps them out of its logs instead: a model's `ToString()`, the JSON effect and the junction logger write each as `{"_omitted": true}`. They are still readable wherever those columns are. Keep a secret out of an input where you can, and pass a reference to it instead.

## On a question type

On the enum or marker type a `Decide`, `Switch`, `Gate` or `Scale` asks about, the mark withholds
the answer. Junction events ([AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events))
and `trax.junction_run` then record that the question was asked and answered, with
`AnswerWithheld` set, but not the option, score, probability, confidence or track taken. The
GraphQL `onJunctionEvent` subscription, `operations.junctionRuns`, the SignalR sink and the
dashboard's timeline show it as withheld.

```csharp
[TraxSensitive]
public enum CreditTier { Prime, NearPrime, Subprime }
```

- It is decided by the type the question is about, with inheritance, so a type that inherits the
  mark is withheld even when it was built at run time or lives in an assembly no scan saw. The key
  is checked as well: a closed form of a marked generic type, a type nested in a marked type, and
  a type that takes a marked type as a type argument are withheld.
- It fails closed: a question whose key shares a name with a marked type is withheld too.
- `TraxRedaction.IsSensitiveQuestion(questionKey)` (namespace `Trax.Effect.Utils`) is the same
  check, from a recorded key alone, for code that reads `trax.decision` or `trax.junction_run`
  back. The scheduler's `GetRecordedDecisionsAsync` withholds answers with it. The marked type has
  to be in an assembly the process has loaded.
- On a type it does nothing else. It does not mask a property of that type in a train's input or
  output; mark the property for that.
- [`trax.decision`](/docs/effect/decisions#recording-decisions) keeps the full answer either way,
  because a requeue or retry replays it from there.
- The path is withheld with the answer: every step after the route (a junction, a question or
  another route) is published and stored with its name as `(withheld)` and `NameWithheld` set, in
  every view, and a later question or route has its key, answer, confidence and decider left out
  too. The number, kinds, positions and timing of those steps, a failed junction's exception type
  and failure class, the run's own failure junction and any train started on the track stay
  visible; see [Withholding an answer](/docs/effect/junction-events#withholding-an-answer).
- The decision journal's log writes the answer, the track and why a recorded answer was not
  replayed as withheld. After a route on a sensitive question, the log also withholds the question
  key, answer, track and decider of every later decision, refusal and route of the run.

## On a member of a decision's state

A recorded decision stores the hash of the state the question was asked about, and that hash
covers every value in the state, marked members included. Without a state hash key, the journal
stores no hash for a question whose state type can reach a member marked `[TraxSensitive]`, so its
answer is never replayed, and logs a warning naming the state type once. Configure a key with
`AddDecisionRecording(o => o.HashStatesWith(key))` or `Trax:Decisions:StateHashKey` to have such
answers replay; see [Keying the state hash](/docs/effect/decisions#keying-the-state-hash).

A state type can reach a marked member through its members and theirs, recursively. A member typed
`object`, an interface, or a class that is not sealed (or a collection of any of these) can hold any
type assignable to it, so the state type can reach a mark when any loaded type assignable to that
member carries one. Types are looked into whatever their namespace; only .NET's own assemblies are
skipped.

## Example

```csharp
using Trax.Effect.Attributes;

public record ConnectAccountInput(
    string AccountId,
    [property: TraxSensitive] string ApiToken
) : IManifestProperties;
```

The run's stored input holds `{"_redacted": true}` where the token would be; the junctions receive the token.

## Package

```
dotnet add package Trax.Effect
```
