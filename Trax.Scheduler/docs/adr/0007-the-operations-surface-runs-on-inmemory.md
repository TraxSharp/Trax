---
authors: [Theauxm]
areas: [scheduling, providers]
status: accepted
---

# The operations surface runs on the InMemory provider

`IOperationsService` and the cancels on `ITraxScheduler` work on every provider the scheduler
accepts, InMemory included. Where they write with a set-based `ExecuteUpdate` (cancelling runs,
cancelling work queue entries, enabling or disabling manifests and groups), they first ask the
data context whether it supports set updates. When it does not, which today means InMemory, they
load the rows the statement would have touched, change them and save once, and return the same
count.

## Status

**Accepted.**

## Considered options

- **Refuse clearly at startup.** An InMemory host that registers the operations surface would
  fail fast with a message. Rejected: InMemory is how tests and small hosts run the scheduler,
  and `AddScheduler()` registers `IOperationsService` there as everywhere, so those hosts would
  lose every batch action for no gain.
- **Per-row everywhere.** One code path for all providers. Rejected: the single statement is
  what keeps a relational write correct against a concurrent writer (a run that finishes, or an
  entry the dispatcher claims, between the read and the write keeps its state), and it is what
  makes a thousand-row batch one round trip.

## Consequences

The per-row path has no guard against a concurrent writer. That is acceptable only because an
InMemory store lives in one process, where the scheduler's own writers do not race an operator
the way a separate dispatcher host does.

This is a narrow exception to [0002](./0002-a-database-provider-is-interchangeable.md): the
difference is the provider's capability, asked of the context, not a provider name, and it stays
inside the operations surface. The polling services that need a relational store are still not
registered on InMemory.

## Exemplars

- `OperationsServiceInMemoryTests` runs every batch method and both scheduler cancels against
  the InMemory provider and asserts the rows, counts and change signals a relational host gets.
- [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service) is the rule this
  produces.

Not covered: nothing finds a new `ExecuteUpdate` or `ExecuteDelete` on the operations surface
that skips the check; only a test that runs the new method on InMemory does.
`ITraxScheduler.ScheduleManyAsync`'s prune still deletes with `ExecuteDelete` and logs a warning
on InMemory; it is scheduling, not an operation.

## Changelog

- **2026-09-27**: Recorded.
