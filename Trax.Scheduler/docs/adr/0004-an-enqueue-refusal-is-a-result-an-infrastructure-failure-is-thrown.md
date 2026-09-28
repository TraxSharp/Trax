---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# An enqueue refusal is a result; an infrastructure failure is thrown

`OperationsService.QueueTrainAsync` backs the GraphQL `queueTrain` and `requeueExecution`
mutations and the dashboard's queue and re-queue buttons. It returns a failed
`OperationResult` with `"The enqueue was refused: {message}"` only when the enqueue was
refused. When the exception chain holds a database, EF Core, network, I/O or timeout failure,
the service logs it and rethrows it unchanged, and its message never becomes a result.

## Status

**Accepted.**

## Why this is written down

Because the catch-all it replaced looked like the friendly option. Every exception except
authorization and cancellation used to become `success: false` with the exception's message
appended. So a database outage reached a GraphQL client as
`"The enqueue was refused: Failed to connect to 10.0.0.5:5432"`. That is two faults. It
reports a server failure as a business answer about the input, and it hands an internal
address to whoever called. Someone who reads the old behaviour as more helpful will want it
back.

## What counts as which

**A refusal** is an answer the caller can act on: the train's `OnQueue` hook or
`QueueSubjectKey` threw, the subject key was empty or too long, or a deferred entry was
cancelled before it was confirmed (`QueuedWorkCancelledException`). The message comes from the
train author or the mediator and is written for the caller. Invalid and oversized input keep
their own messages ahead of this.

**An infrastructure failure** is anything whose chain holds a `DbException` (so every
`NpgsqlException` and `SqliteException`), a `DbUpdateException`, a `TimeoutException`, a
`SocketException`, an `HttpRequestException` or an `IOException`. The chain is walked, inner
exceptions and `AggregateException` members included, because EF Core wraps the provider's
exception and a hook may wrap what it caught.

A data-layer exception is a failure even when a hook's own write caused it, a unique
violation included. A hook that means to refuse throws its own exception. Letting a
constraint violation through as a refusal would put table and constraint names in the
caller's message.

## A run follows the same rule

`OperationsService.RunTrainAsync` (central 0022) applies it to a run. An unknown train and
invalid or oversized input are failed results, with the same messages `QueueTrainAsync` gives.
Nothing a train does can refuse a run, because a run has no `OnQueue` hook and no subject key,
so every other failure is thrown: a job submitter that fails (after the run's metadata row is
marked `Failed`, as the job dispatcher does), a database failure writing the row, and the
missing-enforcer `InvalidOperationException`, which a queue still reports as a refusal (see
Consequences).

## Considered options

**Return a generic failed result instead of throwing** (`"The enqueue failed."`). This keeps
the no-try/catch convenience that `OperationResult` promises, but it is still `success: false`
data. A client cannot tell it from a refusal without matching text, and it hides a server
fault from the GraphQL error channel that monitoring watches. Every other operation in the
service already lets a data failure through. A thrown exception reaches the Trax.Api error
filter, which masks any type it does not know as `"Unexpected Execution Error"`. The dashboard
catches it and shows it to an operator, who is entitled to see it.

**Allow-list the refusals instead of listing the failures.** A hook may throw any type to
refuse, and consumers do, so an allow-list would turn their refusals into masked errors. The
failure list is a closed set of infrastructure types, which a hook author has no reason to
throw on purpose.

## Consequences

**The mediator's missing-enforcer `InvalidOperationException` still reads as a refusal.** It
is a host misconfiguration, not an answer about the input. But it has the same type as a
subject key refusal, and nothing but its text tells them apart. Giving it a type of its own is
a Mediator change.

**Hosts need the error path wired.** A GraphQL client now sees an error, not
`success: false`, when the database is down. A host that turned on HotChocolate's
`IncludeExceptionDetails` in production would pass the message through, which that setting
already does for every other resolver.

## Exemplars

- `OperationsServiceRunTests` pins the same split for a run: failed results for bad input and
  an unknown train, and a thrown, logged submit failure that leaves the run `Failed`.
- `OperationsServiceEnqueueTests` runs the real mediator over a data context that throws
  `NpgsqlException("Failed to connect to 10.0.0.5:5432")` and asserts it is thrown and logged,
  not returned. It also covers an EF-wrapped failure, a timeout wrapped by a hook, and a hook
  refusal and a cancelled deferred entry that still come back as refusals.
- [Mutations: queueTrain](/docs/sdk-reference/graphql-api/mutations#queuetrain) is the rule this
  produces for GraphQL clients.

Not covered: the list of infrastructure types is pinned by example, not exhaustively. A new
data provider whose exceptions derive from none of them would be reported as a refusal. Nothing
in this repo asserts that Trax.Api masks the rethrown exception. That is the error filter's
contract, and Trax.Api's error filter tests pin it.

## Changelog

- **2026-09-27**: Extended to `RunTrainAsync`, where a submit failure and the missing-enforcer
  exception are thrown.
- **2026-09-27**: Recorded.
