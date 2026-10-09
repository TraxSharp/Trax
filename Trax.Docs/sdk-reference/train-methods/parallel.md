---
layout: default
title: Parallel
description: Reference for Parallel, which runs a fixed set of named branches side by side in one run, each on its own copy of Memory, then joins them.
parent: Train Methods
grand_parent: SDK Reference
nav_order: 15
---

# Parallel

Runs a fixed set of named branches side by side within one run, each on its own copy of Memory,
and joins them before the next step. The branches are part of the chain's declaration, so every
one of them is verified at startup.

`Parallel` is experimental. Using it reports the diagnostic `TRAXEXP001` as an error; opt in by
adding it to the project's `NoWarn`:

```xml
<!-- TRAXEXP001: this project uses the experimental Parallel step. -->
<NoWarn>$(NoWarn);TRAXEXP001</NoWarn>
```

## Parallel(branches)

```csharp
protected MonadTask<TInput, TReturn> Parallel(
    Func<Branches<TInput, TReturn>, Branches<TInput, TReturn>> branches
)
```

Also available on the chain itself (`Chain<A>().Parallel(...)`), and inside a track or another
branch.

## Branches\<TInput, TReturn\>

| Method | Description |
|---|---|
| `Branch(string name, Func<MonadTask, MonadTask> body)` | Declares a branch. The name is unique within the step and cannot contain `/` or `#`; it is part of the id of every step in the branch. |
| `OnFailure(BranchFailurePolicy policy)` | `CancelSiblings` (the default) cancels the other branches as soon as one fails; `WaitForAll` lets every branch finish first. |

A branch is written on the parameter it is handed (`b => b.Chain<ScoreEmbedding>()`), the way a
track is.

## How it runs

- **Memory.** Each branch starts from a copy of Memory as it was at the step. When every branch
  has finished, the types each one added are merged into the run's Memory for the steps after the
  join. What a branch decided, and which track it took, stay the branch's.
- **Scope.** Each branch has its own dependency injection scope, so a scoped service (a database
  context, say) is never used from two branches at once. State reached through an ambient context,
  such as the current caller, is the same in every branch. Scoped state you set yourself is copied
  into a branch's scope by an `IBranchScopeInitializer` you register; one that throws fails the
  branch. Branch scopes are disposed when the run ends.
- **Threads.** Every branch starts at once on the thread pool, so a junction that blocks or works
  on the CPU before its first `await` does not hold up its siblings. There is no limit on how many
  run at once: the number is the number you declared. Each branch can hold a database connection.
- **Cancellation.** A junction in a branch is handed the branch's `CancellationToken`, cancelled
  when the run is cancelled and, under `CancelSiblings`, when a sibling fails.

## Failures

A branch that fails fails the step with a `BranchesFailedException`:

| Member | Description |
|---|---|
| `Step` | The step's id, as in `Parallel#0`. |
| `Failures` | Each `FailedBranch` (`Branch`, `Exception`, `FailureClass`), first to fail first. |
| `CancelledBySibling` | Branches stopped because a sibling failed. They are not failures. |
| `Combine(classes)` | The class of several failures: `Permanent` if any is, `Transient` only if every one is. |

The run's recorded failure carries the combined class, so the step is retried as a whole only
when every failure could be. A run that is cancelled while a `Parallel` runs (its token, or its
cancel flag, which raises a `CancellationRequestedException` in the branch that reads it) is
recorded as cancelled, never as failed branches. Any other `OperationCanceledException` a branch
raises on its own, such as an `HttpClient` timeout, is that branch's failure.

## What the startup check refuses

- two branches producing the same type: the join would have two values for one type
- a branch producing a type that was in Memory before the step, a tuple element included
- a branch reading a type only a sibling produces: branches cannot see each other's work
- `ShortCircuit` inside a branch, which would race its siblings for the run's result
- a call on the train itself inside a branch (`Chain<A>()` instead of `b.Chain<A>()`)
- one junction instance handed to two branches
- a branch with no name, a repeated name, or a name containing `/` or `#`

## Branches compute; the join commits

Each branch has its own scope and so its own transaction. Do the work in the branches and write
what must be all-or-nothing in the step after the join.

## Example

```csharp
protected override Task<Either<Exception, TopicMap>> Junctions() =>
    Chain<LoadCorpus>()
        .Parallel(p => p
            .Branch("embedding", b => b.Chain<EmbeddingSimilarity>())
            .Branch("cocitation", b => b.Chain<CoCitationOverlap>())
            .Branch("authors", b => b.Chain<SharedAuthorOverlap>()))
        .Chain<CombineSignals>()   // reads every branch's output, and is the one step that writes
        .Resolve();
```

In the [declared graph](/docs/sdk-reference/train-methods/chain-graph), a `Parallel` step is a
node whose tracks are its branches, and a step in a branch has an id such as
`Parallel#0/cocitation/CoCitationOverlap#0`. While it runs, `ChainGraph.CurrentBranchPath` is
`Parallel#0/cocitation`.
