---
authors: [Theauxm]
repos: [core, effect, scheduler, api, dashboard, samples]
areas: [platform]
status: accepted
---

# A Parallel step runs a fixed set of branches on copies of Memory, and the join commits

A chain may run independent work side by side within one run: `Parallel(p => p.Branch("a", b => …).Branch("b", b => …))`.
The branches are fixed and named when the chain is written, so the chain is still one declaration the host reads and
verifies at startup ([0016](./0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md)). Each branch runs on
a copy of Memory taken at the fork, in its own dependency injection scope, with its own cancellation token; what the
branches add is merged before the next step. Branches compute; the step after the join commits.

## Status

**Accepted.** Ships behind `[Experimental("TRAXEXP001")]` until the `Parallel` row of the interaction matrix
(`Trax.Core/docs/interaction-matrix.md`) is complete. This sets the convention for experimental features: one
diagnostic id per feature, `TRAXEXP` and a number. Using one reports an error, so a project that opts in suppresses
the id with a comment saying why.

## Why this is written down

Because a train has always been a straight line over one Memory, and several things break quietly when two parts of
it run at once. Each rule below is the answer to one of them.

**Memory.** A branch gets a copy of Memory as it was at the fork. Two branches adding the same type would leave the
join two values for one type, so the startup check refuses it, as it refuses a branch producing a type that was in
Memory before the fork and a branch reading a type only a sibling produces (it cannot see it: they run at the same
time, and the container silently answering instead would hide the mistake). What a branch decided and which track it
took stay the branch's, so two branches may route on the same question. `Unit` is not merged. At run time a type two
branches both add anyway (an interface of a tuple element) is left out of the merge rather than taken from either. The
merge is therefore a disjoint union, which is what makes running the branches together equivalent to running them one
after another.

**Scope.** A branch has its own scope, replacing the run's container in its Memory, because a scoped service is not
made to be used from two threads. Ambient state reached through `AsyncLocal` (the caller, a trusted scope) flows into
the branch by itself; scoped state a consumer sets imperatively (a tenant, a unit of work) is copied by an
`IBranchScopeInitializer`, and a branch whose initializer throws fails rather than running without it. Branch scopes
are disposed when the run ends, not at the join, because what a branch put in Memory may still hold on to its scope.
A value handed to `AddServices` is one instance seen by every branch, as it is seen by every step; it has to be safe
to share. A junction instance holds the state of the step it runs, so one instance handed to two branches is refused.

**Failure.** A branch that fails fails the step with a `BranchesFailedException` carrying every branch that failed.
Its class is the join of the branches' classes over `Transient < Conflict < Unclassified < Permanent`: the step can be
retried as a whole only when every failure could be. Under `CancelSiblings` (the default) the other branches are
cancelled when one fails, and a branch stopped that way is listed as cancelled by its sibling, not as a failure;
`WaitForAll` lets every branch finish. A cancellation that came from outside the step (the caller, the dashboard's
cancel flag, a timeout) cancels the run, never fails it.

**Execution.** Every branch starts on the thread pool at once, so a junction that blocks or does CPU work before its
first `await` cannot hold up its siblings. There is no concurrency limit: the branches are fixed in the code, and each
may hold a database connection, which matters for trains with many branches times the scheduler's active jobs. A
junction in a branch honours the branch's token. A short circuit sets the run's result while the chain runs on, so in
a branch it would race its siblings and is refused; so is a call on the train itself inside a branch, which would run
on the run's Memory beside the branch.

**Commits.** Separate scopes mean separate transactions, so writes that must be all-or-nothing go after the join.
The run's own tracked writes commit once, when it finishes (effect/0021).

**Ids.** A branch's name is part of the id of every step in it (`Parallel#0/cocitation/ScoreCoCitation#0`), so a run's
recorded steps land on the branch they ran in, and adding a branch moves no other id.

## Considered options

**Sub-trains, one child run per branch.** Rejected for a fixed set of branches: each branch would become a separate
run with its own row, retries and queue entry, which is the right shape for fan-out over data known only at run time,
and the wrong one for three signals computed side by side in one run.

**Only transient services in branches.** Rejected: a transient can still capture a scoped database context, and
singletons are already shared everywhere, so they already have to be thread-safe. A scope per branch is the rule that
actually separates them.

**A concurrency limit.** Rejected while the branches are fixed in the code: the author already chose how many there
are.

## Exemplars

**Enforced elsewhere:** `ParallelTests` in `Trax.Core/tests/Trax.Core.Tests.Unit` pins the merge, the refusals, the
failure and cancellation rules, scopes and ids; the interleaving and law tests beside it compare every interleaving
of small cases against running the branches one after another; Trax.Effect's junction event and decision tests cover
branches recording concurrently; `InteractionMatrixTests` in `Trax.Core.Tests.Meta` requires the `Parallel` row of
the interaction matrix to name a test for every existing feature.

Not covered: nothing can check that a junction leaves objects it was handed unchanged. The equivalence of running
branches together and one after another holds only while junctions follow that convention.

## Changelog

- **2026-10-07**: Recorded.
