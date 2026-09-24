---
authors: [Theauxm]
repos: [mediator, scheduler, api, dashboard]
areas: [platform, graphql]
status: accepted
---

# A caller's enqueue goes through the mediator

Anything that enqueues a train with a train and input a caller chose goes through
`ITrainExecutionService.QueueAsync`, which applies the train's `[TraxAuthorize]` requirements,
its `OnQueue` hook and its subject key. Only system-initiated enqueues (the ManifestManager, and
dormant dependents a parent train activates) and admin-surface actions on manifests (trigger,
dead-letter requeue) may build a work queue row themselves; the admin actions are governed by
the admin surface's gate as a whole rather than by each train's requirements.

The dashboard is the admin surface. Its queue dialog and Re-queue button go through
`QueueAsync` inside a trusted execution scope (`"dashboard"`), so `OnQueue`, the subject key and
the input cap apply but per-train `[TraxAuthorize]` does not: the dashboard is gated as a whole
by its host. Its Run dialog submits directly to the job submitter.

## Status

**Accepted.**

## Considered options

**Authorizing each surface where it enqueues.** What the code did: the operations service,
the queue dialog and the re-queue button each wrote their own row, and only some of them
checked anything. Every new surface was a new chance to forget, and three of them had. Routing
through one method makes the check impossible to skip by writing a new caller.

**Applying per-train requirements to manifest triggers and dead-letter requeues too.** Rejected
for now. Those paths let the caller choose which manifest, never the train or the input, and
they are reachable only through the admin surface. The admin surface is meant to carry admin
privileges as a whole, and is expected to gain user-provided authorization (Microsoft Entra or
another provider) rather than a per-train check bolted onto each admin action. Until then its
posture is `GateOperations`, `RequireAuthorization` or `AllowAnonymousOperations` (api/0004).

**Applying per-train requirements to the dashboard.** Rejected. A Blazor Server circuit has no
HTTP request behind a click, so the enforcer, which reads the user from the request, refused
every `[TraxAuthorize]` train from the dashboard, for admins too. Per-user checks belong to the
admin surface's own authorization when it arrives, not to a per-train check the dashboard cannot
satisfy.

**Checking authorization inside `TraxScheduler`.** Rejected because `TraxScheduler` is also
called from a host's own background code, where there is no user; a check there would refuse
every such call for a train with requirements.

## Consequences

The operations service authorizes before it reads the caller's input, so a caller who may not
run a train learns nothing about the input it expects from a parse error. `OperationsService`
takes `ITrainExecutionService` as a required dependency, so an API-only host needs
`AddMediator` as well. The mediator's runtime fail-closed check for a `[TraxAuthorize]` train with no
enforcer registered honours a trusted scope the same way the enforcer does. That exemption only
matters where hosted services do not run (the Lambda runner, a bare `ServiceProvider`): in a
hosted app, `AuthorizationRegistrationValidator` refuses to start a host with `[TraxAuthorize]`
trains and no `ITrainAuthorizationService` unless it calls `AllowMissingAuthorizationService()`
(mediator/0001), so a dashboard-only or scheduler-only host needs that opt-out or an enforcer.

**The dashboard's trusted scope extends into consumer code.** The scope is an `AsyncLocal`, and the
dashboard's enqueue runs the train's `OnQueue` hook and `QueueSubjectKey` inside it. Anything
those run or enqueue through `ITrainExecutionService` is therefore trusted too and skips that
train's `[TraxAuthorize]` requirements, and code reading `TraxCaller.IsTrusted` sees `true`. That
includes work the hook starts with `Task.Run` or any other call that captures the execution
context, which keeps the scope after the dashboard's enqueue has returned. This is a known
consequence of gating the dashboard as a whole rather than per train, and is recorded rather than
closed: the scope marks the whole async flow, and nothing separates the dashboard's own call from
consumer code running inside it. A hook that must not act as trusted has to check
`ITrustedExecutionScope.IsTrusted` itself.

A dormant dependent's input is chosen at runtime by the parent train's code
(`IDormantDependentContext.ActivateAsync(externalId, input)`), not fixed by a manifest, and its
entry skips `QueueSubjectKey` and `OnQueue`. It is system-initiated work running inside a train
that was itself authorized or scheduled, which is why it may build its own row.

## Exemplars

**Enforced elsewhere:** `WorkQueueCreationSitesTests` in the `Tests.Meta` projects of
Trax.Scheduler (allow-list: the ManifestManager's scheduled enqueue, dormant dependents, and
`TraxScheduler`'s manifest trigger and dead-letter requeue), Trax.Api and Trax.Dashboard (no
allowed sites). `OperationsServiceAuthorizationTests` in Trax.Scheduler pins the refusal itself, running the
operations service against the real mediator. `QueueTrainAuthorizationTests` in Trax.Api pins
only the error shape, the `TRAX_AUTHORIZATION` error `queueTrain` returns.

Not covered: nothing checks that the dashboard is actually gated by its host, which is what its
trusted scope assumes. The guards find `WorkQueue.Create` by text, so a row built another way, or through
a helper outside `src`, would pass. Nothing checks that the manifest paths stay behind the admin
gate; that posture is api/0004's.

## Changelog

- **2026-09-23**: Recorded that the dashboard's trusted scope flows into the `OnQueue` hook and
  `QueueSubjectKey`, so trains they run or enqueue, including from `Task.Run`, skip their own
  `[TraxAuthorize]`.
- **2026-09-23**: Recorded.
