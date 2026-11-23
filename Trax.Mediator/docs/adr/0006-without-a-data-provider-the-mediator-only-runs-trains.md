---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# Without a data provider, the mediator only runs trains

`AddMediator` registers the train bus, the registry, discovery and the startup checks on every
host. The services that read or write the run-record tables (`ITrainExecutionService`,
`IRunStarters`, `IRunExecutor`, `IWorkQueuePromotion` and `IInvokedTrainLauncher`) each need an
`IDataContextProviderFactory`, which only a data provider registers, so `AddMediator` registers
them only when the effects have one (`UsePostgres`, `UseSqlite` or `UseInMemory`). A host that
only runs trains in process, with `AddEffects()` and no provider, gets the bus and nothing it
cannot build.

## Status

**Accepted.**

## Considered options

**Register them always.** What 1.71 did. Nothing resolves them on such a host, so production
starts, but a container that validates itself (`ValidateOnBuild`, on by default in Development)
cannot build them and the host refuses to start.

**Refuse `AddMediator` without a data provider.** It would make every in-process-only host pay
for a database it never writes to, or for `UseInMemory`, which keeps a metadata row for every run
until the process exits.

**Register stand-ins that throw when used.** The container builds, but a call that needs a
database fails at request time instead of at startup, and the startup checks that look for
`ITrainExecutionService` (`AddTraxGraphQL`'s operations surface, a state machine that invokes
trains) would find one and pass.

## Consequences

Whether a data provider was chosen is read from the builder, not from the service collection:
effects are configured before `AddMediator` in the fluent chain, so the answer is settled when it
runs and does not depend on the caller's ordering.

`AddServiceTrainBus`, which takes a bare `IServiceCollection` and cannot see the builder, still
registers all of them; it already required effects to be configured.

Anything that needs them refuses to start without them, and now says to add a data provider:
`AddScheduler`, `AddTraxGraphQL`'s operations surface, and a state machine that invokes trains.

## Exemplars

- `MediatorWithoutDataProviderTests` builds a mediator host with no data provider under
  `ValidateOnBuild` and `ValidateScopes`, starts its hosted services, resolves the bus, and checks
  that none of the five services is registered, and that all five are with a data provider.

Not covered: `AddServiceTrainBus` on its own, which keeps registering them.

## Changelog

- **2026-11-23**: Recorded.
