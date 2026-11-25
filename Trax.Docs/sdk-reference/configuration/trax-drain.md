---
title: TraxDrain
description: Reference for --trax-drain, --trax-resume and TRAX_DRAIN_FORCE, which stop every host from starting new work before a deploy replaces them.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 17
package: Trax.Effect
namespace: Trax.Effect.Configuration.Deploy
source:
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/TraxDrain.cs
  - Trax.Effect/src/Trax.Effect/Configuration/Deploy/DrainServeGate.cs
  - Trax.Effect/src/Trax.Effect/Models/Drain/Drain.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/Drain/DrainCommand.cs
  - Trax.Effect/src/Trax.Effect.Data/Services/Drain/DrainStore.cs
  - Trax.Effect/src/Trax.Effect.Data/Extensions/TraxDeployExtensions.cs
---

# TraxDrain

A drain stops every host that shares the database from starting new work, until a time. A deployment drains before it
replaces the hosts, waits for what is running to finish, then rolls. It starts the new version's image once with
`--trax-drain`, the way it starts it with `--trax-migrate`, so the drain is set from inside the cluster or the ECS
network and needs no endpoint into the app. An operator can drain and resume from the console or the API as well.

## Members

`TraxDrain`, in `Trax.Effect.Configuration.Deploy` (package `Trax.Effect`):

| Member | What it is |
|---|---|
| `Argument` | `--trax-drain`, optionally followed by a timeout (`--trax-drain 00:10:00` or `--trax-drain=00:10:00`). The process sets the drain, waits for busy work, and exits through `RunTraxAsync`. |
| `ResumeArgument` | `--trax-resume`. The process ends the drain and exits. |
| `ForceVariable` | `TRAX_DRAIN_FORCE`. `true` makes a drain past its timeout exit as though drained, so the deploy rolls anyway. Empty or `false` does not; any other value refuses to start. |
| `DeployActor` | `deploy`, what a drain a deploy set records as `StartedBy`. |
| `DefaultTimeout` | 10 minutes, the wait when `--trax-drain` names none. |
| `Margin` | 10 minutes. A deploy's drain lapses at its timeout plus this, time for the roll. |
| `MaximumTimeout` | 24 hours. |
| `Requested` | Whether this process's command line contains `--trax-drain` or `--trax-resume`. |
| `RequestedIn(args)`, `ResumeRequestedIn(args)` | The same for `args`, and for `--trax-resume` alone. |
| `TimeoutIn(args)` | The timeout `args` ask for: the value after `--trax-drain`, `DefaultTimeout` without one, null without the argument. A value that is not a time span from one second to a day, or both arguments at once, throws `ArgumentException`. |
| `ForceRequested(value)` | Reads `TRAX_DRAIN_FORCE`'s value. |
| `ParseTimeout(value, paramName)` | Reads a timeout written as a .NET time span. |

The row is `Drain`, in `Trax.Effect.Models.Drain`, on `IDataContext.Drains`: `Id` (always `SingletonId`, 1), `Until`,
`StartedAt` and `StartedBy`. The table is `trax.drain` ([schema migrations](/docs/reference/schema-migrations)).

## Behavior

While the row exists and `Until` is ahead of a host's clock:

| Part of a host | While drained |
|---|---|
| the job dispatcher | dispatches nothing. Its loop keeps running and its health check stays green. |
| the manifest manager | queues no scheduled or dependent run. Its reapers keep running, so a run past its timeout still ends. A schedule that comes due is a misfire once the drain ends: the manifest's `MisfirePolicy` (or the scheduler's default) decides whether it runs then. |
| a local worker | claims no job. The check is inside the claim's transaction. |
| a queued run, a trigger, a dead-letter requeue | is written to the work queue as usual and waits there. |
| a train a request runs inline, a machine's timer or policy | runs. The drain waits for the run it starts like any other. |

What a drain waits on, its busy list, is:

- every run that is Pending or InProgress, except a Pending run whose job is still in the queue unclaimed (it waits in
  the database through the roll and the next hosts run it) and the scheduler's own trains;
- every state-machine instance that is not at rest: one with an effect in flight on it, or one whose invoked run is
  busy by the rule above.

A process started with `--trax-drain`:

1. sets the drain to lapse at now plus the timeout plus `Margin`, replacing any drain in force;
2. waits 5 seconds, so a host that read the row just before it was set finishes its cycle and its work shows;
3. reads the busy list every 2 seconds. When it is empty, the process exits and leaves the drain in place for the
   roll.
4. Past the timeout, it ends the drain and throws, naming what is busy (the first 50, and how many more), so the
   process exits non-zero and the deploy refuses. With `TRAX_DRAIN_FORCE=true` it logs the list instead and exits as
   though drained, with the drain kept.

Cancelled or killed, it leaves the drain to lapse at its time. A process started with `--trax-resume` deletes the row.
A scheduler host (one that calls `AddScheduler`) that starts while a drain is in force skips its stuck-run recovery,
since the hosts it replaces may still be finishing runs, and leaves the drain alone: the deploy ends it once every
service has rolled, and a deploy that died leaves one that lapses at its time.

Neither process migrates the database, whatever `TRAX_MIGRATIONS` says, and one started with both `--trax-migrate` and
`--trax-drain` refuses to start. A host started with either argument that calls `app.Run()` rather than
`RunTraxAsync` refuses to start, naming `RunTraxAsync`, so a drain process never runs a scheduler beside the hosts it
drains.

Every provider keeps the row: Postgres, SQLite and InMemory. A host compares `Until` with its own clock.

## Example

```bash
# What trax deploy runs, in the cluster, with the new version's image:
dotnet App.dll --trax-drain 00:10:00     # exits 0 once nothing is busy, non-zero past the timeout
dotnet App.dll --trax-resume             # after the roll
```

## See also

- [Drain before a deploy](/docs/reference/drain-before-a-deploy)
- [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate), [IOperationsService](/docs/sdk-reference/scheduler-api/i-operations-service)
