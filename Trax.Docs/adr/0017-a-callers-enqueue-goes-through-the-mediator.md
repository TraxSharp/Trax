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

The operations console is a client of the API's operations namespace. Its queue, re-queue and run
actions are `queueTrain`, `requeueExecution` and `runTrain`, which go through `QueueAsync` (or the
mediator's run) under the caller's own principal, so the train's `[TraxAuthorize]` requirements
apply to it as to any caller. No code opens a trusted scope for it.

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
posture is `GateOperations`, `GateOperationsToAuthenticatedUsers` or `AllowAnonymousOperations`
(api/0004).

**Exempting the dashboard from per-train requirements.** What this recorded until 2026-10-23: a
Blazor Server circuit has no HTTP request behind a click, so the enforcer could not read the user,
and the dashboard enqueued inside a trusted scope instead. The Blazor dashboard is gone; the
console that replaced it sends each action as a GraphQL request, so the exemption has nothing left
to cover.

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
(mediator/0001), so a scheduler-only host needs that opt-out or an enforcer.

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

Not covered: the guards find `WorkQueue.Create` by text, so a call through a helper outside `src` would pass.
`WorkQueue`'s parameterless constructor is protected, so there is no other way to build a row. Nothing checks that the manifest paths stay behind the admin
gate; that posture is api/0004's.

## Changelog

- **2026-10-23**: The dashboard's trusted scope is no longer described: the Blazor dashboard that
  opened it is gone, and the operations console's actions go through the API's operations under the
  caller's own per-train authorization.
- **2026-10-23**: The admin surface's posture no longer includes `RequireAuthorization` (api/0004).
- **2026-10-01**: The dashboard's Run dialog calls `IOperationsService.RunTrainAsync` inside the
  trusted scope instead of submitting to the job submitter itself.
- **2026-09-24**: `WorkQueue.Create` is now the only way to build a row, so the guards' text
  match no longer misses an object initializer.
- **2026-09-23**: Recorded that the dashboard's trusted scope flows into the `OnQueue` hook and
  `QueueSubjectKey`, so trains they run or enqueue, including from `Task.Run`, skip their own
  `[TraxAuthorize]`.
- **2026-09-23**: Recorded.
