---
authors: [Theauxm]
areas: [scheduling, platform]
status: accepted
---

# An enqueue refusal is a result; an infrastructure failure is thrown

`OperationsService.QueueTrainAsync` backs the GraphQL `queueTrain` and `requeueExecution`
mutations and the dashboard's queue and re-queue buttons. It returns a failed
`OperationResult` only when the enqueue was refused, and the refusal's message reaches the
caller only when it was written for the caller: `"The enqueue was refused: {message}"` for a
plain `TrainException` and the mediator's own refusals, the fixed `"The enqueue was refused."`
for anything else. When the exception chain holds a database, EF Core, network, I/O or timeout failure,
or the enqueue failed because the host has no authorization enforcer, the service logs it and
rethrows it unchanged, and its message never becomes a result.

## Status

**Accepted.** Refined 2026-09-30: which refusals show their message is an allow-list (see
*What a refusal shows*).

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
cancelled before it was confirmed (`QueuedWorkCancelledException`). Invalid and oversized
input keep their own messages ahead of this.

**An infrastructure failure** is anything whose chain holds a `DbException` (so every
`NpgsqlException` and `SqliteException`), a `DbUpdateException`, a `TimeoutException`, a
`SocketException`, an `HttpRequestException` or an `IOException`. The chain is walked, inner
exceptions and `AggregateException` members included, because EF Core wraps the provider's
exception and a hook may wrap what it caught.

**A host misconfiguration** is the mediator's `TrainAuthorizationNotConfiguredException`: the
train declares `[TraxAuthorize]` and the host registered no `ITrainAuthorizationService`. It is
thrown like an infrastructure failure. It is caught by its type, which the mediator gave it for
this purpose, so no message text is matched. The mediator's `DecisionReplayNotSupportedException`
is the same kind of fault: a re-queue that replays the run's decisions (central 0041) reached a
custom or decorating `ITrainExecutionService` that does not implement the overload carrying the
replay link. It names that implementation, and it is logged and thrown the same way.

A data-layer exception is a failure even when a hook's own write caused it, a unique
violation included. A hook that means to refuse throws its own exception. Letting a
constraint violation through as a refusal would put table and constraint names in the
caller's message.

## What a refusal shows

A refusal is always a failed result, whatever type was thrown. Its message is shown only when
something wrote it for the caller: a plain `TrainException` (its exact type, not a type derived
from it), which is the train author's, and the mediator's `QueuedWorkCancelledException` and
`QueueHookTimeoutException`, whose text the mediator builds from the train's name, an entry id
or the configured limit. Every other type gets the fixed `"The enqueue was refused."`, and the
exception is logged at Warning so an operator still sees it.

This is the rule central `docs/0028` applies to a remote run's `PublicMessage`, for the same
reason: a hook calls into whatever it depends on, and the message of an exception it lets
through was written by that dependency for its own operator, not for the caller. A hook author who wants the caller to read the reason throws `TrainException`.

## A run follows the same rule

`OperationsService.RunTrainAsync` (central 0022) applies it to a run. An unknown train and
invalid or oversized input are failed results, with the same messages `QueueTrainAsync` gives.
A run applies the per-record checks a queue applies (central 0037), so the train's `OnQueue`
hook can refuse it, and that refusal is split and shown exactly as an enqueue's is, as
`"The run was refused…"`; a subject-keyed train run outside a trusted scope is a failed result
too. Every other failure is thrown: a job submitter that fails (after the run's metadata row is
marked `Failed`, as the job dispatcher does), a database failure writing the row, and the
missing-enforcer `TrainAuthorizationNotConfiguredException`. A run reads its input and checks
its authorization through the mediator's `PrepareAsync`, so both answers are the mediator's.

## Considered options

**Return a generic failed result instead of throwing** (`"The enqueue failed."`). This keeps
the no-try/catch convenience that `OperationResult` promises, but it is still `success: false`
data. A client cannot tell it from a refusal without matching text, and it hides a server
fault from the GraphQL error channel that monitoring watches. Every other operation in the
service already lets a data failure through. A thrown exception reaches the Trax.Api error
filter, which masks any type it does not know as `"Unexpected Execution Error"`. The dashboard
catches it and shows it to an operator, who is entitled to see it.

**Allow-list the refusals instead of listing the failures.** A hook may throw any type to
refuse, and consumers do, so an allow-list deciding *whether* it is a refusal would turn their
refusals into thrown, masked errors. The failure list is a closed set of infrastructure types,
which a hook author has no reason to throw on purpose, so it still decides that. What is
allow-listed instead, since 2026-09-30, is only which refusals *show their message*: a refusal
of another type stays a refusal, with a fixed message.

## Consequences

**A missing enforcer is a server fault on both paths.** The mediator throws
`TrainAuthorizationNotConfiguredException` (Trax.Mediator 1.23.1 and later), which derives from
`InvalidOperationException`, the type a subject key refusal also has. The catch for it sits
ahead of the refusal catch, so a queue no longer reports it as `"The enqueue was refused"`.
Until the mediator gave it a type of its own, a queue did.

**Hosts need the error path wired.** A GraphQL client now sees an error, not
`success: false`, when the database is down. A host that turned on HotChocolate's
`IncludeExceptionDetails` in production would pass the message through, which that setting
already does for every other resolver.

## Exemplars

- `OperationsServiceRunTests` pins the same split for a run: failed results for bad input and
  an unknown train, a thrown missing-enforcer exception, and a thrown, logged submit failure
  that leaves the run `Failed`.
- `OperationsServiceEnqueueTests` runs the real mediator over a data context that throws
  `NpgsqlException("Failed to connect to 10.0.0.5:5432")` and asserts it is thrown and logged,
  not returned. It also covers an EF-wrapped failure, a timeout wrapped by a hook, a missing
  enforcer that is thrown and logged, and a hook refusal and a cancelled deferred entry that
  still come back as refusals. It pins the allow-list too: a plain `TrainException` and a hook
  timeout show their message, while an `InvalidOperationException` and a type derived from
  `TrainException` come back with the fixed message and are logged.
- `OperationsServiceRequeueTests` pins the replay misconfiguration: a re-queue of a run that
  recorded decisions, through an `ITrainExecutionService` without the overload that carries the
  link, throws and logs `DecisionReplayNotSupportedException` and queues nothing.
- [Mutations: queueTrain](/docs/sdk-reference/graphql-api/mutations#queuetrain) is the rule this
  produces for GraphQL clients.

Not covered: the list of infrastructure types is pinned by example, not exhaustively. A new
data provider whose exceptions derive from none of them would be reported as a refusal. Nothing
in this repo asserts that Trax.Api masks the rethrown exception. That is the error filter's
contract, and Trax.Api's error filter tests pin it.

## Changelog

- **2026-10-01**: `DecisionReplayNotSupportedException` is a host misconfiguration too, logged and
  thrown from `RequeueExecutionAsync` rather than reported as `"The enqueue was refused."`.
- **2026-09-30**: A run's `OnQueue` refusal follows the enqueue's rule (central 0037).
- **2026-09-30**: A refusal shows its exception's message only for a plain `TrainException`,
  `QueuedWorkCancelledException` and `QueueHookTimeoutException`; any other type is a refusal
  with the fixed `"The enqueue was refused."`, logged at Warning. The same rule as central
  `docs/0028`.
- **2026-09-30**: A missing authorization enforcer is thrown on the queue path too, caught by
  the mediator's `TrainAuthorizationNotConfiguredException`; a run takes its authorization and
  input reading from the mediator's `PrepareAsync`.
- **2026-09-27**: Extended to `RunTrainAsync`, where a submit failure and the missing-enforcer
  exception are thrown.
- **2026-09-27**: Recorded.
