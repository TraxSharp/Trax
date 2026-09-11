---
authors: [Theauxm]
repos: [effect, mediator, scheduler, dashboard, api]
areas: [naming, platform]
status: accepted
---

# The canonical train name is the interface FullName

A train is identified everywhere by the FullName of its service interface, for example
`Trax.Samples.GameServer.Trains.Combat.IResolveCombatTrain`. Registration through
`AddScopedTraxRoute<TService, TImpl>()` sets `CanonicalName = typeof(TService).FullName`,
`ServiceTrain.TrainName` returns it, and `metadata.Name`, `manifest.Name` and
`work_queue.train_name` all store it.

## Status

**Accepted.**

## Considered options

**The concrete type's FullName.** What `GetType().FullName` gives you, and the value you
get by accident when you forget the registration carries a canonical name. It breaks the
moment an implementation is renamed or swapped, because rows already written still name the
old class.

**The short name.** Readable, and used deliberately in log messages for that reason.
Rejected as an identifier because short names collide across namespaces, and a collision
here silently merges two trains' history.

## Consequences

**`GetType()` is the wrong call for anything stored, compared, or used as a key.** It
returns the concrete type. Use `ServiceType` from the registration, or `CanonicalName` /
`TrainName` from the `ServiceTrain`.

**A comparison against `metadata.Name` must use `.FullName`**, not `.Name`. Comparing
stored interface names against a concrete type's FullName fails silently: the lookup simply
finds nothing, and the feature looks broken rather than misconfigured.

**Exclusions and whitelists take the interface.** When a caller writes
`ExcludeFromMaxActiveJobs<TTrain>()` or `ScheduleAsync<TTrain>()`, `TTrain` is the
interface, and the system stores `typeof(TTrain).FullName!`.

## Exemplars

- [Train Discovery](/docs/mediator/train-discovery) documents the three-tier name
  resolution a consumer sees.
- [Metadata](/docs/effect/metadata) shows the stored name on the persisted record.

**Enforced elsewhere:** `InterfaceFullNameInvariantTests` in `Trax.Mediator`'s `Tests.Meta`
project, which walks the layers that must agree (metadata, work queue, manifest, GraphQL
hooks, dashboard requeue, scheduler exclusions) and fails when one drifts to a concrete or
short name.

Not covered: nothing stops a consumer registering a train without an interface, which
leaves `CanonicalName` null and falls back to the concrete FullName.
`TrainGuards.EveryTrainHasInterface` is the opt-in check for that, and it ships rather than
being applied here.

## Changelog

- **2026-09-11**: Recorded.
