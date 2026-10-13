---
title: Train overhead
description: "What the benchmark numbers mean in practice: the fixed cost of a train, why it vanishes behind I/O, and the work trains are not built for."
type: explanation
area: general
nav_order: 8
---

# Train overhead

A train costs something on every invocation, and the [Benchmarks](/docs/reference/benchmarks) page
has the numbers. Read relative to plain code they look enormous: a `Train` that adds one to its
input is about 6,900 times slower than `input + 1`. That comparison says more about `input + 1`,
which finishes in a fraction of a nanosecond, than about the train.

## A fixed cost, not a multiplier

In absolute terms a `Train` adds about 1.5 μs per invocation, and a `ServiceTrain` with no effect
providers about 7 μs, most of which is creating the DI scope and running the effect lifecycle
(initialize and save). Each junction adds about 200 ns on top. None of this grows with the work the
junctions do, so the more each junction does, the smaller the share the train takes.

The simulated I/O benchmark shows the effect: three `Task.Yield` calls bring the base train to about
3.6 times the serial cost, down from 6,900 times. A junction that queries a database (1 to 10 ms) or
calls an HTTP service (50 to 500 ms) puts the train's 1.5 to 7 μs below 0.01% of the run.

## What trains are for

Trains are built for business operations where each junction does meaningful work: database
queries, API calls, file I/O, domain logic. What they buy at that scale is the railway error
handling, the recorded run and the composition, at a cost that is noise next to the I/O. They run
comfortably at request granularity, tens to hundreds per second per process.

They are not built for hot paths:

- per-element processing inside a large collection, where a loop or LINQ does the job;
- anything that runs millions of times per second;
- pure computation where each nanosecond counts.

In those places the fixed microseconds dominate, and the run record, which a `ServiceTrain` writes
when a database data provider is registered, adds a database write per call on top. Keep that work inside
a junction, called once per train, rather than making each element a train.
