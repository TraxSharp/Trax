---
layout: default
title: Checkpoint
description: Reference for Checkpoint, which stores a state the chain declares so a later run of the same input resumes after it instead of running every step again.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 16
---

# Checkpoint

Stores a state the chain declares, so a later run of the same input resumes after it instead of
running every step before it again. A run that fails at its last junction no longer repeats the
expensive ones before it.

`Checkpoint` is experimental. Using it reports the diagnostic `TRAXEXP003` as an error; opt in by
adding it to the project's `NoWarn`:

```xml
<!-- TRAXEXP003: this project uses the experimental Checkpoint step. -->
<NoWarn>$(NoWarn);TRAXEXP003</NoWarn>
```

## Checkpoint\<TState\>()

```csharp
protected MonadTask<TInput, TReturn> Checkpoint<TState>()
```

Also available on the chain itself (`Chain<A>().Checkpoint<TState>()`), inside a track and inside
a `Parallel` branch.

When the run reaches the step, the `TState` in Memory is stored, with the track each routing step
before it took. Nothing else in Memory is stored: services, data contexts and objects changed in
place do not survive being written down and read back. Choose a `TState` that holds what the steps
after the checkpoint need, as pointers (ids, fingerprints, dataset URIs) rather than rows.

A run with no data provider (a plain `Train`, or a host without `UsePostgres`, `UseSqlite` or
`UseInMemory`) takes no checkpoint, and the step does nothing.

## What a state must be

The state is stored as JSON and read back, so it must come back as the same value. The startup
check refuses, naming the member:

- a class that is not sealed, at any depth: a derived value would be read back as its base type
- a member typed `object`, or an interface other than a framework collection
- a field: the serializer writes properties only
- a property with no public setter, `init` accessor or constructor parameter of its name
- a property marked `[JsonIgnore]`

It also refuses a state that reaches a member marked `[TraxSensitive]`, and a checkpoint inside a
track whose routing key is sensitive, whose route is withheld from every record. A sealed record of
data is always accepted:

```csharp
public sealed record CheckedFindings(Findings Findings, string Depth);
```

## When it is written

A checkpoint is written through a data context of its own, in its own transaction, and commits
nothing else the run tracks. Writing one fails the step, classified permanent, and stores nothing,
when:

- the step's Trax data context still holds unsaved changes or an open transaction: a run resumed
  after the checkpoint would skip work that never committed. Commit first, or move the checkpoint
  after the commit.
- the state's JSON is larger than the cap a requeue's stored input has: four times the mediator's
  `MaxInputJsonBytes`, 1 MiB by default.

A run that completes deletes its checkpoints. A failed or cancelled run's checkpoints are deleted
with it by metadata cleanup, which keeps a run while a queued resume still needs it.

## Resuming

A run resumes from an earlier run of the same input that failed or was cancelled:

| How | Resumes |
|---|---|
| A manifest's retry | After the failed run's latest checkpoint, when it has one. Otherwise from the top, as before. |
| A dead letter's requeue | The same. |
| `requeueExecution` | Never: it runs the train again from the top. |
| [`resumeExecution(id, from)`](/docs/sdk-reference/graphql-api/mutations#resumeexecution), or the dashboard's **Resume** and **Resume from here** | At the step `from` names, or after the latest checkpoint when `from` is omitted. |
| A state machine entering an invoking state again | Never: the state is the stage's checkpoint. |

The resumed run runs `Junctions()` again and skips every step before its resume point in place: no
junction runs and no decider is asked, but each step keeps the node id and asking it had, so the
decisions after the point replay from the run it resumes. The checkpoint's state and routes go into
Memory, beside the run's input and its services. A resumed run that fails again resumes from the
same checkpoint next time, not from the top.

Steps after the checkpoint run again on every resume, so they must be idempotent, as every retried
step must. Anything that must not happen twice takes its own claim.

### What can resume where

Whether a run can resume at a step is decided from the declared chain before anything runs. Every
step from the resume point on must find its inputs in what the checkpoint restores, the train's
input, the container, or a step after the point. A refusal names the step and the type:

| Code | Why |
|---|---|
| `no-checkpoint` | The run wrote no checkpoint before the point. |
| `missing-input` | A step needs a type nothing restores or produces, as in "`Summarize#0` needs `Findings`". |
| `stale-value` | A step reads a type a skipped step overwrote, which the checkpoint does not hold. |
| `off-the-path` | The point is in a track of a routing step after the checkpoint, which the resumed run would not ask. |
| `inside-a-branch` | The point is inside a `Parallel` branch: resume at the `Parallel`, and each branch resumes from its own checkpoint. |
| `chain-changed` | The chain's hash differs from the one stored with the checkpoint: a deploy changed it. |
| `state-changed` | The state type's shape differs from the one stored, so reading it back would quietly default members. |

A decision made before the checkpoint is not stored: a step after it that reads one makes the
check refuse. Put what it needs in the state.

A retry or a dead letter whose run can no longer resume (a deploy changed the chain, say) runs from
the top and logs why.

### Inside a Parallel

A checkpoint in a branch is stored under the branch's path. A resume into a failed `Parallel` runs
each branch from its own latest checkpoint, and from its start when it has none. A branch whose
last step is a checkpoint it reached runs nothing, and gives the join only what its checkpoint
holds:

```csharp
.Parallel(p => p
    .Branch("embedding", b => b.Chain<EmbeddingSimilarity>().Checkpoint<EmbeddingScores>())
    .Branch("cocitation", b => b.Chain<CoCitationOverlap>()))
.Chain<CombineSignals>()
```

## Example

```csharp
protected override Task<Either<Exception, ResearchReport>> Junctions() =>
    Chain<PlanResearch>()
        .Switch<ResearchBrief, Source>(s => s
            .When(Source.Web, w => w.Chain<SearchWeb>())
            .When(Source.Papers, p => p.Chain<SearchPapers>()))
        .Scale<Findings, Depth>(s => s
            .AtLeast(Depth.Skim, k => k.Chain<SkimSources>())
            .AtLeast(Depth.CrossCheck, c => c.Chain<FetchFullTexts>()))
        .Checkpoint<CheckedFindings>()   // a crash in Summarize no longer fetches again
        .Chain<Summarize>()
        .Resolve();
```

In the [declared graph](/docs/sdk-reference/train-methods/chain-graph), a checkpoint is a node of
kind `Checkpoint` whose input is its state, with an id such as `Checkpoint<CheckedFindings>#0`.
