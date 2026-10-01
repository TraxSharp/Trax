---
authors: [Theauxm]
repos: [scheduler, api, dashboard]
areas: [platform, graphql]
status: accepted
---

# The dashboard and the API share one operation per action

Every action an operator can take in Trax.Dashboard has a counterpart in the GraphQL
`operations` namespace, and both call the same method underneath: `IOperationsService` or
`ITraxScheduler` in Trax.Scheduler, or the mediator where the action is a train's own
execution. Neither surface carries its own validation, input reading, authorization or
persistence for an action. The GraphQL API is then enough to build another frontend on, the
React dashboard among them, with nothing the Blazor dashboard can do that it cannot.

## Status

**Accepted.**

## Why this is written down

Because the two surfaces kept drifting, and each drift was a bug on one side only. The
dashboard's re-queue deserializes and re-serializes the saved input while the API passes it
through, so the dashboard re-queues a truncated input with default values that the API
refuses. The Run dialog wrote its metadata row before submitting and left it Pending when the
submit failed, read enum inputs one way on one tab and another on the other, and ignored the
submitter a train is routed to. The API had no run at all. None of that is visible from
either surface alone.

## Considered options

**Each surface implements the action itself.** What the code did for re-queue and run. It is
quicker for the first surface and wrong by the second, because nothing makes the copies agree.

**The dashboard calls its own GraphQL API over HTTP.** Rejected. The dashboard is Blazor
Server running in the host's process, often in a host that serves no API, and a circuit has
no HTTP request whose caller an HTTP hop could carry. It would add authentication plumbing to
reach code it can already call.

**Share at the mediator.** Only for execution. The admin state (the work queue, runs,
manifests, dead letters, scheduler settings) belongs to the Scheduler, so the shared method
for everything but a train's own execution lives in `IOperationsService`.

## Consequences

**A new action lands in three places together**: the operations method, the GraphQL field that
calls it, and the dashboard control that calls it. A new API field is also measured at scale in
`Trax.Api.Tests.Stress`, at millions of rows, because another frontend will page and poll it
the way the dashboard does.

**Surfaces differ only in who the caller is.** The dashboard calls inside a trusted scope, gated
as a whole by its host (0017); the API calls as the request's user. The operation decides what
a refusal is and what is thrown (scheduler/0004), so both surfaces report the same failure the
same way.

**Where each action stands.** The rule is met when the shared method exists and both surfaces
call it. As of this ADR's last changelog entry:

- *Met:* queue a train, cancel one work queue entry, edit a group's settings, edit the scheduler
  settings, dead-letter re-queue and acknowledge, manifest and group trigger, the dashboard
  metrics and the group dependency graphs.
- *Shared method exists, surfaces not switched yet* (both wait on the Scheduler release that
  carries it): run a train (`RunTrainAsync`); cancel a list of runs (`CancelExecutionsAsync`, the
  rule `ITraxScheduler.CancelAsync` and `CancelGroupAsync` now share); cancel a list of work
  queue entries; enable or disable a list of manifests, a list of groups, or every group; manifest
  stats, group stats and paged logs.
- *No shared method yet:* re-queue an execution, editing a manifest (`updateManifest` writes the
  row in the API), the executions, work queue and dead-letter list reads, and the persisted
  operations reads. The group detail page's "Cancel All Running" also repeats `CancelGroupAsync`'s
  query instead of calling it, which needs no release to fix.

Anything in the last two groups is a defect against this decision, not an exception to it.

**`RunTrainAsync` takes the mediator's rules rather than copying them.** It once repeated the
mediator's authorization check and input reading, because the mediator kept both private. The
mediator now exposes them as `ITrainExecutionService.PrepareAsync`, which the run path calls, so
a run and a queue of the same train and JSON cannot drift apart. It also applies the per-record
checks a queue applies, the train's `OnQueue` hook and its subject key
([0037](./0037-run-now-applies-the-same-per-record-checks-as-queueing.md)).

**`IOperationsService` grows.** A member added to it is a break for anyone implementing the
interface themselves, so a new member has a default implementation that throws
`NotSupportedException`.

## Exemplars

**Enforced elsewhere:** `OperationsServiceRunTests` in Trax.Scheduler pins the run path the two
surfaces share, and `OperationsServiceEnqueueTests` the queue path. `WorkQueueCreationSitesTests`
in Trax.Dashboard and Trax.Api allows neither surface to build a work queue row itself.

Not covered: nothing checks that a dashboard page calls the operations service rather than a
data context or a job submitter, and nothing checks that each dashboard action has a GraphQL
field. Parity is held by review and by the audit list, not by a guard.

## Changelog

- **2026-09-30**: `RunTrainAsync` applies the per-record checks a queue applies (0037).
- **2026-09-30**: `RunTrainAsync` calls the mediator's `PrepareAsync` instead of copying its
  authorization check and input reading.
- **2026-09-27**: Listed which actions comply, which have a shared method the surfaces do not
  call yet, and which have none.
- **2026-09-27**: Recorded.
