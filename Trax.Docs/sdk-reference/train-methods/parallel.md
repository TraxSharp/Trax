---
title: Parallel
description: "Reference for Parallel, which runs a fixed set of named branches side by side in one run, each on its own copy of Memory, then joins them."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
  - Trax.Core.Exceptions
source:
  - Trax.Core/src/Trax.Core/Monad/Monad.Parallel.cs
  - Trax.Core/src/Trax.Core/Monad/Branches.cs
  - Trax.Core/src/Trax.Core/Exceptions/BranchesFailedException.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 15
---

# Parallel

Runs a fixed set of named branches side by side within one run, each on its own copy of Memory,
and joins them before the next step. The branches are part of the chain's declaration, so every
one of them is verified at startup.

## Signature

```csharp fragment
// Train<TInput, TReturn>; MonadTask<TInput, TReturn> and Monad<TInput, TReturn> have the same, public
protected MonadTask<TInput, TReturn> Parallel(
    Func<Branches<TInput, TReturn>, Branches<TInput, TReturn>> branches
);

namespace Trax.Core.Monad;

public sealed class Branches<TInput, TReturn>
{
    public Branches<TInput, TReturn> Branch(string name, Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> body);
    public Branches<TInput, TReturn> OnFailure(BranchFailurePolicy policy);
}

public enum BranchFailurePolicy
{
    CancelSiblings = 0,
    WaitForAll = 1,
}

public interface IBranchScopeInitializer
{
    Task Initialize(IServiceProvider run, IServiceProvider branch, CancellationToken cancellationToken);
}
```

## Parameters

| Parameter | Description |
|---|---|
| `branches` | Declares the branches on the `Branches<TInput, TReturn>` it is handed |

| `Branches` method | Description |
|---|---|
| `Branch(string name, Func<MonadTask, MonadTask> body)` | Declares a branch. The name is unique within the step and cannot contain `/` or `#`; it is part of the id of every step in the branch. A branch is written on the parameter it is handed (`b => b.Chain<ScoreEmbedding>()`), the way a track is. |
| `OnFailure(BranchFailurePolicy policy)` | `CancelSiblings` (the default) cancels the other branches as soon as one fails; `WaitForAll` lets every branch finish first. |

## Returns

`MonadTask<TInput, TReturn>`, the next link of the chain. `Parallel` is also available inside a track and inside another branch.

## Behavior

### How it runs

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

### Failures

A branch that fails fails the step with a `BranchesFailedException`:

| Member | Description |
|---|---|
| `Step` | The step's id, as in `Parallel#0`. |
| `Failures` | Each `FailedBranch` (`Branch`, `Exception`, `FailureClass`), in the order the step recorded them. Branches failing at the same moment are ordered as the step happened to see them. |
| `CancelledBySibling` | Branches that failed after a sibling's failure cancelled them, whatever they threw. They are not failures. |
| `Combine(classes)` | The class of several failures: `Permanent` if any is, `Transient` only if every one is. |

The run's recorded failure carries the combined class, so the step is retried as a whole only
when every failure could be. A run that is cancelled while a `Parallel` runs (its token, or its
cancel flag, which raises a `CancellationRequestedException` in the branch that reads it) is
recorded as cancelled, never as failed branches. Any other `OperationCanceledException` a branch
raises on its own, such as an `HttpClient` timeout, is that branch's failure.

### Logs and traces

Each branch runs under a logger scope and an `Activity` that carry its branch path (`Parallel#0/cocitation`), so the
lines and spans of two branches running side by side can be told apart. Both flow into the branch's junctions.

| Name | Value |
| --- | --- |
| `BranchDiagnostics.SourceName` | `"Trax.Core"`, the `ActivitySource` the branch activities come from; add it to a tracer to collect them |
| `BranchDiagnostics.BranchPathKey` | `"trax.branch"`, the activity's tag and the logger scope's key holding the path |

The scope is opened through the logger factory in Memory or the container; a train with neither logs as before. A
provider that prints scopes shows `Parallel branch Parallel#0/cocitation`.

### What the startup check refuses

- two branches producing the same type: the join would have two values for one type
- a branch producing a type that was in Memory before the step, a tuple element included
- a branch reading a type only a sibling produces: branches cannot see each other's work
- `ShortCircuit` inside a branch, which would race its siblings for the run's result
- a call on the train itself inside a branch (`Chain<A>()` instead of `b.Chain<A>()`)
- one junction instance handed to two branches (one the declaration cannot see, found by `IChain`
  in Memory or registered as a singleton, fails the step when a second branch runs it)
- a branch with no name, a repeated name, or a name containing `/` or `#`

### In the declared graph

In the [declared graph](/docs/sdk-reference/train-methods/chain-graph), a `Parallel` step is a
node whose tracks are its branches, and a step in a branch has an id such as
`Parallel#0/cocitation/CoCitationOverlap#0`. While it runs, `ChainGraph.CurrentBranchPath` is
`Parallel#0/cocitation`.

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

public class MapTopicsTrain : ServiceTrain<CorpusRequest, TopicMap>
{
    protected override Task<Either<Exception, TopicMap>> Junctions() =>
        Chain<LoadCorpus>()
            .Parallel(p => p
                .Branch("embedding", b => b.Chain<EmbeddingSimilarity>())
                .Branch("cocitation", b => b.Chain<CoCitationOverlap>())
                .Branch("authors", b => b.Chain<SharedAuthorOverlap>()))
            .Chain<CombineSignals>()   // reads every branch's output
            .Resolve();
}
```

## See also

- [Checkpoints and branches](/docs/core/checkpoints-and-branches), what to do in a branch and what after the join
- [Checkpoint](/docs/sdk-reference/train-methods/checkpoint), inside a branch
- [ChainGraph](/docs/sdk-reference/train-methods/chain-graph)
