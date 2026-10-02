---
layout: default
title: Troubleshooting
description: The error messages Trax raises at registration, startup and dispatch, quoted verbatim, each with its causes and fix, plus problems that raise no error.
parent: Cross-Cutting
nav_order: 4
---

# Troubleshooting

Each error entry is headed by the message Trax raises, quoted as it appears in the source, with
the names it fills in shown as `X`, `Y` and `Z`. Search this page for a distinctive phrase from
the message you have. Problems that raise no error are at the end.

## "Could not find train with input type (X)"

The `TrainBus` has no train registered for the input's type, and throws `NoTrainForInputException`. The message names the type, the assemblies the mediator scanned, and the two fixes:

```text
Could not find train with input type (MyApp.Orders.OrderInput): no IServiceTrain<OrderInput, TOut> is registered for it. Scanned assemblies: [MyApp.Api]. Add a train that takes this input type, or add the assembly that holds its train to ScanAssemblies(...).
```

**Causes:**
- The assembly containing your train is not in the scanned list, because it was never passed to `AddMediator` or `ScanAssemblies`
- Your train doesn't implement `IServiceTrain<TIn, TOut>`
- Your train class is `abstract`

**Fix:** add the train's assembly to the scan.
```csharp
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly, typeof(YourTrain).Assembly))
);
```

This is a host configuration error, reported to the host. The exception is not a `TrainException`, so Trax.Api and the scheduler's runner endpoints, which pass a `TrainException`'s message through as a train's own words, do not pass this one through that way; read it in the host's log. A caller that asks `ITrainExecutionService` for a train name that does not exist gets `TrainNotFoundException` instead, whose message is always "The requested train was not found." and does not say what is registered.

The scheduler checks the same thing when a train is scheduled (`Schedule`, `ScheduleMany`, `ScheduleOnceAsync` and the rest) and throws `InvalidOperationException` with the same fix:

```text
No train implements IServiceTrain<OrderInput, TOut>. Add the train's assembly to AddMediator(m => m.ScanAssemblies(typeof(MyTrain).Assembly)).
```

A scheduled run runs the train it names, so the scheduler also refuses a train that was not scanned when another train takes the same input type: `Train 'MyApp.Orders.IOrderTrain' is not registered, although another train takes OrderInput.`, followed by the same fix.

## "AddTraxDashboard() requires AddTrax() to be called first"

`AddTraxDashboard()` checks, when it is called, that `AddTrax()` has already registered Trax in
the same `IServiceCollection`. The full message is "AddTraxDashboard() requires AddTrax() to be
called first. Call services.AddTrax(trax => ...) before services.AddTraxDashboard()."

**Cause:** `AddTrax()` was not called, or it was called after `AddTraxDashboard()`.

**Fix:** Call `AddTrax()` first:
```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);

builder.Services.AddTraxDashboard(o => o.RequireRoles("Admin"));   // After AddTrax()
```

## "AddTraxGraphQL() requires AddTrax() to be called first"

The same check in `AddTraxGraphQL()`, with the same fix: call
`services.AddTrax(trax => ...)` before `services.AddTraxGraphQL(...)`.

## "AddTraxDashboard() has already been called on this host"

`AddTraxDashboard()` was called twice. A second call would replace the first one's authorization
settings, so a shared bootstrap that allows anonymous access could silently undo a host's
`RequireRoles(...)`. Call it once, with every dashboard option in that call.

## "Call AddEffects(...) before AddMediator(...)."

The step builder pattern enforces configuration ordering at compile time. `AddMediator()` is only available on `TraxBuilderWithEffects` (returned by `AddEffects()`), and `AddScheduler()` is only available on `TraxBuilderWithMediator` (returned by `AddMediator()`).

**Cause:** Calling methods out of order. Trax.Mediator and Trax.Scheduler report order mistakes as CS0619 with the fix as the text:

| Error text | Cause |
|---|---|
| `Call AddEffects(...) before AddMediator(...).` | `AddMediator()` called before `AddEffects()` |
| `AddMediator(...) is already called. Call it once and configure everything in that call.` | `AddMediator()` called twice |
| `Call AddStateMachines(...) before AddMediator(...).` | `AddStateMachines()` called after `AddMediator()` |
| `Call AddMediator(...) before AddScheduler(...).` | `AddScheduler()` called before `AddMediator()`, straight after `AddEffects()` or on the bare builder |
| `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).` | `AddDataContextLogging()` called before a data provider |

**Fix:** Follow the required order: `AddEffects()` -> `AddStateMachines()` if you use it -> `AddMediator()` -> `AddScheduler()`:
```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))  // Step 1
    .AddMediator(typeof(Program).Assembly)                         // Step 2
    .AddScheduler()                                                // Step 3
);
```

## "AddScheduler() requires a data provider (UsePostgres(), UseSqlite(), or UseInMemory())"

The scheduler's background services keep manifests, Metadata and work queue entries in the
database, so `AddScheduler()` refuses a host whose `AddEffects(...)` names no data provider.

**Fix:** add one, from its own package (`Trax.Effect.Data.Postgres`, `.Sqlite` or `.InMemory`):
```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString)) // or UseSqlite(...) / UseInMemory()
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);
```

## "AddJunctionProgress() requires a data provider (UsePostgres(), UseSqlite(), or UseInMemory())"

Junction progress writes the current junction to the run's Metadata row and reads cancellation
requests from it, so it needs a data provider in the same `AddEffects(...)` call. Add one as above.

## "AddStateMachines() requires a data provider"

The state-machine snapshot store keeps drafts and effect claims in the database. Configure a data
provider in `AddEffects(...)` before calling `AddStateMachines(...)`.

## "N of M registered trains cannot run:"

The host refused to start. The mediator's startup check builds every registered train and replays
its chain before the host serves traffic, and found trains that would fail every run. Each
indented line below the heading names one train and one problem, in the form
`IMyTrain: step 2 (MyJunction) needs 'Customer' in Memory and nothing before it puts one there.`
The entries that follow cover each kind of line. A train the check cannot build outside a request
is not refused: it is logged as a warning, "The chain of X was not verified at startup".

## "X cannot be built: its constructor needs 'Y', which is not registered"

The host refused to start because a train's own constructor asks for a type that nothing registers
in the container, so the train would fail every run. Register the type before building the host.
The same refusal names a registered dependency whose own constructor needs an unregistered type,
with the path the train reaches it through: `ProbeRepository needs 'IClock', which is not
registered, and the train's constructor reaches it through 'IProbeRepository'.` A train class with
no public constructor is refused as `FooTrain has no public constructor. Give it one.` A dependency
registered through a factory that can only build inside a request (one reading `HttpContext`, say)
is not refused: the train is skipped with a warning, "was not verified at startup".

## "Junction 'X' (train 'Y') needs 'Z' as a constructor argument"

A junction's constructor asks for something the chain never produced and the container does not
hold. Trax builds a junction through its single public constructor, taking each argument from
Memory first and the container second, so the message names the junction, the train and the
missing type.

**Fix:** Register the missing service, or chain a junction that outputs it before this one. On a
generic host the startup chain check finds this before the first run and refuses to start, with
`step N (X) needs 'Z' as a constructor argument`. The run-time message above is what a host that
skips the check (the Lambda runner, a bare `ServiceProvider`, `SkipChainVerification()`) sees.

## "Junction 'X' (train 'Y') has 2 public constructors"

Trax builds a junction through its single public constructor. A junction with more than one, or
none, cannot be built, so the startup chain check refuses the host naming the junction and the
count. Give it exactly one public constructor.

## "Junction 'X' (train 'Y') needs 'Z' as its input, but nothing earlier in the chain produced one"

The chain could not find a value of the junction's input type in Memory, and the container does
not register one either. The startup check reports the same problem as
`step N (X) needs 'Z' in Memory and nothing before it puts one there.`

**Causes:**
- A previous junction didn't return or add the expected type to Memory
- Type mismatch between junction output and next junction's input

**Fix:** Check the chain flow. Each junction's input type must exist in Memory (seeded automatically from the train input, or from a previous junction's output):
```csharp
// Memory starts with: { CreateUserRequest, Unit }
Chain<ValidateJunction>()              // Takes CreateUserRequest, returns Unit
    .Chain<CreateUserJunction>()       // Takes CreateUserRequest, returns User
    .Chain<SendEmailJunction>();       // Takes User (from previous junction)
```

The [startup chain verification](/docs/core/trains-and-junctions#the-host-checks-every-chain-before-it-serves-traffic) catches these before the host serves traffic: it refuses to start and names the junction and the missing type. (The compile-time [Analyzer](/docs/core/analyzer) is deprecated and no longer reports CHAIN001.)

## "Unable to resolve service for type 'X' while attempting to activate 'Y'"

This message comes from the .NET DI container, not from Trax: something it is building, usually a
train or a service the train depends on, asks for a type nothing registers. The startup check
reports a train's unregistered dependency itself, as "X cannot be built" above, so this raw message
reaches you only for a train the check did not verify: one it skipped with a "was not verified at
startup" warning, or a host where the check does not run (the Lambda runner, a bare
`ServiceProvider`, `SkipChainVerification()`).

**Fix:** register the missing type before building the host:
```csharp
services.AddScoped<IUserRepository, UserRepository>();
```

## "Manifest group 'X' is given two different MaxActiveJobs values"

Two schedules that share a group each state the same group setting (`MaxActiveJobs`, `Priority` or `Enabled`) with different values. Every start writes a stated group setting, so the value in force would depend on which manifest was seeded last, and `AddScheduler` refuses it. The message names both schedules.

**Fix:** state the setting on one member of the group, or the same value on each. A member that says only `.Group("name")` leaves the group's settings alone.

## "Batch 'X' prunes manifests whose external ID starts with ..."

One batch's prune prefix starts another batch's, so the first would delete the second's manifests at every start. A name-based `ScheduleMany(name, ...)` prunes only within its own group, so this is raised only when the groups do not keep the two apart: an explicit `PrunePrefix`, or two name-based batches moved into one group.

**Fix:** rename one batch so neither name plus `-` starts the other, or keep the batches in separate groups.

The same message, ending "which includes '...', scheduled on its own", means a single `Schedule` falls inside a batch's prune: its external ID starts with the batch's prefix and, for a name-based batch, it is in the batch's group (`Schedule("sync-extra", ..., o => o.Group("sync"))` beside `ScheduleMany("sync", ...)`, or a plain `Schedule("sync-extra")` beside a batch with `PrunePrefix("sync-")`). Each start the batch would delete it with its history and its own schedule would create it again.

**Fix:** give the single schedule an external ID outside the prefix, put it in a different group (name-based batches only), or make it one of the batch's items.

## "Invalid cron expression: 'X'. Expected 5 or 6 space-separated fields."

`Schedule.FromCron`, which every `Cron` helper goes through, parses the expression when the
schedule is created and throws this `FormatException` for an expression with the wrong number of
fields, such as a seven-field one. An expression with the right number of fields but a value out of
range, such as `Cron.Daily(hour: 25)` or `Cron.Hourly(minute: 60)`, is refused by the Cronos parser
with its own `CronFormatException`, which is also a `FormatException`. Fix the value at the call the
stack trace names.

## "Cron expression 'X' never fires: it has no occurrence in the next 10 years."

The expression parses but names a date that does not exist, such as `0 0 30 2 *` (February 30th).
Check the day of the month against the months it names.

## "A schedule interval must be at least one second."

`Schedule.FromInterval`, and so every `Every` helper, throws this `ArgumentOutOfRangeException`
for an interval shorter than one second, including `Every.Seconds(0)`. The manifest stores whole
seconds, so a shorter interval would be stored as zero.

## "The type or namespace name 'IManifestProperties' could not be found"

The compiler's CS0246. `IManifestProperties` lives in the `Trax.Effect` package, not in
`Trax.Scheduler`, in the namespace `Trax.Effect.Models.Manifest`.

**Fix:**
```csharp
using Trax.Effect.Models.Manifest;
```

## Train completes but metadata shows "Failed"

Check `FailureException` and `FailureReason` in the metadata record for details. `FailureJunction` identifies which junction threw, and `StackTrace` points to the original throw site. Common causes:
- An effect provider failed during `SaveChanges` (database connection, serialization error)
- A junction threw after the main train logic completed

If you catch the exception outside Trax, you'll see the original exception type and message. Structured junction context is available via `exception.Data["TrainExceptionData"]`.

## Junctions execute out of order or skip unexpectedly

If you're using `ShortCircuit`, remember that throwing an exception means "continue" not "stop." See [ShortCircuit](/docs/core/building-chains#shortcircuit) for details or [SDK Reference: ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) for all overloads.

## Scheduled jobs don't execute (no errors)

Possible causes:
- The manifest's `IsEnabled` is `false`. Check via `ITraxScheduler` or the database. A disabled manifest's already-queued scheduled entries also wait, `Queued`, until it is re-enabled; only a trigger or a dead-letter requeue runs it while disabled. A restart does not re-enable a manifest or group that was disabled at runtime unless the code states `.Enabled(true)` (see [What a Restart Rewrites](/docs/scheduler/scheduling-options#what-a-restart-rewrites))
- A new cron schedule has not reached its first occurrence yet. It first runs at its first occurrence after it was scheduled, not on the next poll, and cron times are UTC: `Cron.Daily(hour: 3)` is 03:00 UTC. The manifest's `NextScheduledRun` shows when that is
- `ManifestManagerPollingInterval` or `JobDispatcherPollingInterval` is set too high and the job hasn't been picked up yet
- The train's input type doesn't implement `IManifestProperties`
- Your train assembly isn't registered with `AddMediator()`. Make sure to pass the assembly containing your trains

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)
