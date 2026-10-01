---
authors: [Theauxm]
repos: [scheduler, api, dashboard]
areas: [platform, graphql]
status: accepted
---

# Run-now applies the same per-record checks as queueing

`IOperationsService.RunTrainAsync`, the one run operation the dashboard and the API share
([0022](./0022-the-dashboard-and-the-api-share-one-operation-per-action.md)), applies the checks a
queue applies to the individual record, not only the train's `[TraxAuthorize]` requirements. The
train's `OnQueue` hook runs on the run's input before anything is written, and a refusal from it
is returned the way a queue returns one, with no metadata row. A train that declares
`QueueSubjectKey` is run now only inside a trusted scope; outside one it is refused with a
message telling the caller to queue it instead.

## Status

**Accepted.**

## Why this is written down

Because a run and a queue of the same train should be two ways to reach the same checks.
`[TraxAuthorize]` decides whether a caller may use a train at all. Whether the
caller may use it *for this record* is decided in `OnQueue`, which is where the authorization
guide tells a train author to put that check, and `QueueSubjectKey` is what keeps two pieces of
work for one subject apart ([0019](./0019-queued-work-for-one-subject-runs-one-at-a-time.md)). A
train's per-record rules should not depend on which operation the caller picked. Someone who reads the hook as queue-only bookkeeping will want to take it back off the
run path.

## How the hook runs on a run

As the mediator runs it on an enqueue: on an instance resolved through the train's service
interface in a scope of its own, with `TrainInput` reading the run's input, with a metadata
whose `ExternalId` is the one the run executes under, within `MaxQueueHookDuration`, and inside a
trusted scope too, since trust skips authorization and nothing else. Writes the hook makes on
`IEnqueueContextAccessor.Current` are saved with the run's metadata row, so a refusal leaves
neither. What a refusal shows follows scheduler/0004: only a plain `TrainException`'s message
reaches the caller.

A subject key has nothing to do on a run: there is no queue entry to stamp and no dispatch to
hold back. The run cannot be serialized against the subject's queued work, so it is refused
rather than run unserialized. A trusted caller, the dashboard's operator among them, may still
run it, as it may bypass every other queue rule.

## Considered options

**Refuse, outside a trusted scope, any train that overrides `OnQueue` or `QueueSubjectKey`.**
Safe and simple, and it keeps the hook queue-only. Rejected for the hook because it makes run-now
unavailable to exactly the trains that check their records, when running the check answers the
question directly. Kept for the subject key, where there is no check to run.

**Allow run-now only inside a trusted scope.** The smallest change. Rejected because the API's
run operation exists for callers who are not trusted, whose train authorization already applies,
and it would leave that operation useful only to the dashboard.

**Call the mediator to run the hook.** Preferred, and not available: the mediator keeps its hook
invocation private and exposes only `PrepareAsync`, which authorizes and reads the input. The
scheduler therefore runs the hook itself, with the mediator's override check and the same
conventions. If the mediator exposes the invocation, the scheduler should call it instead.

## Consequences

**`OnQueue` runs for a run too.** A hook that does queue-only work, such as a shadow write it
expects a queue entry to follow, now also runs before a run. Its `metadata.ExternalId` is the
run's, so a side effect it records correlates with the run the same way it does with a queued
entry's run.

**A nested enqueue does not join.** An enqueue a hook starts during a run commits on its own
rather than joining the run's write, because the mediator's joining (mediator/0003) is private to
its enqueue.

**A subject-keyed train needs a trusted scope to run now.** An API caller queues it instead.

## Exemplars

**Enforced elsewhere:** `OperationsServiceRunQueueChecksTests` in Trax.Scheduler's
`Trax.Scheduler.Tests.Integration` pins a hook refusal (no row written, trusted scope or not), an
accepted run under the hook's ExternalId, the subject-keyed refusal outside a trusted scope and
the run inside one, and a refusal of another type shown with the fixed message.

Not covered: nothing checks that a new run path added to the operations service calls the same
checks.

## Changelog

- **2026-09-30**: Recorded.
