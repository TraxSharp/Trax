---
authors: [Theauxm]
repos: [effect, mediator, scheduler, dashboard, api, samples]
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

**Enforced elsewhere:** the rule is checked at its source and at six of the seven places it
reaches. InterfaceFullNameInvariantTests in Trax.Mediator registers a fake and asserts
CanonicalName is the interface FullName at the point of registration. Downstream,
PostgresContextTests in Trax.Mediator covers metadata.Name against a real database;
TraxSchedulerCoverageGapTests and OperationsServiceTests in Trax.Scheduler cover manifest.Name
and work_queue.train_name; GraphQLSubscriptionHookTests in Trax.Api covers the hooks including
the negative case, where an implementation-type name is skipped;
SchedulerConfigurationBuilderSettingsTests covers the scheduler exclusions; and in Trax.Samples
ChatLifecycleHookTests and JobHuntLifecycleHookTests cover the consumer side, where a hook
keys a dictionary on `typeof(ITrain).FullName!` and looks it up by `metadata.Name`, negative
case included.

Not covered:

- **Dashboard requeue is the one layer nothing checks.** It compares against the stored name
  in `MetadataDetailPage`, and a drift there is invisible.
- Registering a train without its own interface does not leave `CanonicalName` null: it falls
  back to the first interface, which for an interfaceless train is `IServiceTrain<TIn, TOut>`.
  `TrainGuards.EveryTrainHasInterface` is the check for that, and Trax.Samples applies it to
  Bookworm.

## Changelog

- **2026-09-11**: Added `samples` to `repos`. Two sample lifecycle hooks key a dictionary on
  `typeof(I...Train).FullName!` and look it up by `metadata.Name`, which is exactly the
  comparison the Consequences section warns about, so the decision binds that repo too. The
  two hook test classes that cover it are now named in Exemplars.
- **2026-09-11**: Recorded.
