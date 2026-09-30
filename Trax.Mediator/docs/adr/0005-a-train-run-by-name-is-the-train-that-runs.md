---
authors: [Theauxm]
areas: [auth, platform]
status: accepted
---

# A train run by name is the train that runs

Two trains may take the same input type, and the scheduler's own trains do. `ITrainBus.RunAsync`
picks a train by the input's type, so it can reach only one of them, and it stays that way. Every
path that starts from a train's name works per train instead: discovery lists each train with the
service type and implementation of that one train, and a run by name resolves that registration's
own service type through `ITrainBus.RunByNameAsync`. The train a caller looked up, and was
authorized for, is the train that runs.

## Status

**Accepted.**

## Considered options

**Refuse two trains with one input type.** It would make the input-type lookup unambiguous, but
the scheduler's own trains share `Unit`, as does any consumer train that takes no input, so it
would refuse hosts that work today and push every such train into a wrapper input type.

## Consequences

Discovery lists more trains than it did: every train sharing an input type now appears, so the
startup chain check and the missing-enforcer check see them all, and listings of trains grow
accordingly. `LocalRunExecutor` runs by name, so a name it is given must be a discovered train's
service type full name. Trax.Scheduler's dispatch is bound by it too: it runs a scheduled train
through `RunByNameAsync` with the run's metadata name.

## Exemplars

- `SharedInputTypeExecutionTests` runs each of two trains sharing an input type by name, through
  `ITrainExecutionService`, `LocalRunExecutor` and the bus, and checks discovery lists and pairs
  both, in both registration orders.

Not covered: the input-keyed `RunAsync` still reaches one train per input type by design, and
nothing stops code calling it for an input type two trains share.

## Changelog

- **2026-09-30**: Recorded.
