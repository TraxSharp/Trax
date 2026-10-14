---
title: Checkpoints and branches
description: "Why a checkpoint stores a declared state rather than Memory, what follows for its contents and placement, and why Parallel branches leave writes to the join."
type: explanation
area: trains
nav_order: 7
---

# Checkpoints and branches

A [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) and a [Parallel](/docs/sdk-reference/train-methods/parallel) step both change what a run can assume about the work before it. A checkpoint lets a later run skip that work; a parallel step splits it across scopes that cannot see each other. Both are part of the declared chain, so the startup check knows where they are, but neither can see what a junction does to a database. The guidance below follows from that.

## A checkpoint stores a state, not Memory

When a run reaches a checkpoint, the one value the chain names, `TState`, is written as JSON, with the tracks the routing steps before it took. The rest of Memory is not. Memory holds services, data contexts and objects that junctions changed in place, and none of those survives being written down and read back as the same thing. Storing a declared type instead keeps the resume honest: the startup check can tell, from the chain alone, whether every step after the resume point finds what it needs in the state, the input, the container or a later step. Trax.Docs ADR 0047 records the decision.

That makes the state a contract between the steps before the checkpoint and the steps after it. A state that holds what the later steps need as pointers (ids, fingerprints, dataset URIs) stays small, stays under the size cap, and reads back the same after a deploy. A state that holds rows is a copy of data that may have changed by the time the run resumes, and a change to the row's shape changes the state's shape, which refuses the resume (`state-changed`).

## A checkpoint promises that the work before it committed

A resumed run skips every step before its resume point. If one of those steps wrote to the database without committing, the resumed run skips work that never happened. That is why a checkpoint refuses to write while the step's Trax data context holds unsaved changes or an open transaction. The fix is in the chain's order: commit in a junction before the checkpoint, or place the checkpoint after the commit.

The steps after a checkpoint are the opposite case. They run again on every resume, as they would on any retry, so they have to tolerate running twice. Work that must not happen twice (a payment, an email) takes its own claim, so that a second run sees the first one's record and does nothing.

## Branches compute; the join commits

Each `Parallel` branch runs in its own dependency injection scope, and so with its own data context and its own transaction. The branches cannot see each other's work, and nothing coordinates their commits: if one branch commits and a sibling fails, the step fails with one branch's writes already in the database.

So the work a branch does is computation: fetch, score, compare, and return a value. The step after the join reads every branch's value from Memory and is the one place that writes what must be all-or-nothing. A checkpoint inside a branch fits the same shape, because it stores the value the branch computed, not anything the branch wrote.

## Related

- [Checkpoint](/docs/sdk-reference/train-methods/checkpoint), what is stored, when it is written and when a run can resume
- [Parallel](/docs/sdk-reference/train-methods/parallel), how branches run and fail
- [Trains and Junctions](/docs/core/trains-and-junctions)
