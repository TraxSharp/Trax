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
project. Read it before relying on it: it registers a local fake and asserts that
`CanonicalName` is the interface FullName at the point of registration. That is the
**source** of the rule, and it is all that is checked.

Not covered, and this is most of the decision:

- **None of the six downstream layers is verified.** `metadata.Name`, `work_queue.train_name`,
  `manifest.Name`, the GraphQL hooks, dashboard requeue and scheduler exclusions appear in
  that guard only as prose. Nothing reads or compares them. Three of the six live in repos
  that are downstream of Trax.Mediator and structurally invisible from it
  ([0003](./0003-a-repo-depends-only-on-what-is-upstream.md)); the other three are reachable
  and still unchecked.
- Nothing stops a consumer registering a train without an interface, which leaves
  `CanonicalName` null and falls back to the concrete FullName.
  `TrainGuards.EveryTrainHasInterface` is the opt-in check for that, and it ships for
  consumers rather than being applied across these repos.

## Changelog

- **2026-09-11**: Corrected the enforcement claim: the guard checks CanonicalName at
  registration and none of the six downstream layers the ADR had said it walked.
- **2026-09-11**: Recorded.
