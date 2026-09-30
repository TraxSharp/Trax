---
layout: default
title: Troubleshooting
parent: Cross-Cutting
nav_order: 4
---

# Troubleshooting

## "Could not find train with input type (X)"

The `TrainBus` has no train registered for the input's type. The message names the type, the assemblies the mediator scanned, and the two fixes:

```text
Could not find train with input type (MyApp.Orders.OrderInput): no IServiceTrain<OrderInput, TOut> is registered for it. Scanned assemblies: [MyApp.Api]. Add a train that takes this input type, or add the assembly that holds its train to ScanAssemblies(...).
```

**Causes:**
- The assembly containing your train is not in the scanned list, because it was never passed to `AddMediator` or `ScanAssemblies`
- Your train doesn't implement `IServiceTrain<TIn, TOut>`
- Your train class is `abstract`

**Fix:** add the train's assembly to the scan.
```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly, typeof(YourTrain).Assembly))
);
```

This is a host configuration error, reported to the host. A caller that asks `ITrainExecutionService` for a train name that does not exist gets `TrainNotFoundException` instead, whose message is always "The requested train was not found." and does not say what is registered.

The scheduler checks the same thing when a train is scheduled (`Schedule`, `ScheduleMany`, `ScheduleOnceAsync` and the rest) and throws `InvalidOperationException` with the same fix:

```text
No train implements IServiceTrain<OrderInput, TOut>. Add the train's assembly to AddMediator(m => m.ScanAssemblies(typeof(MyTrain).Assembly)).
```

A scheduled run runs the train it names, so the scheduler also refuses a train that was not scanned when another train takes the same input type: `Train 'MyApp.Orders.IOrderTrain' is not registered, although another train takes OrderInput.`, followed by the same fix.

## "AddTrax() must be called before AddTraxDashboard()" / "...before AddTraxGraphQL()"

`AddTraxDashboard()` and `AddTraxGraphQL()` require `AddTrax()` to be called first. They check for a `TraxMarker` singleton in the DI container at registration time.

**Cause:** `AddTrax()` was not called, or it was called after `AddTraxDashboard()` / `AddTraxGraphQL()`.

**Fix:** Call `AddTrax()` before `AddTraxDashboard()` or `AddTraxGraphQL()`:
```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxDashboard();   // After AddTrax()
builder.Services.AddTraxGraphQL();     // After AddTrax()
```

## Compile error: "Call AddEffects(...) before AddMediator(...)"

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

## "Unable to resolve service for type 'IJunction'"

A junction's dependency isn't registered in the DI container.

**Cause:** Your junction injects a service that wasn't added to `IServiceCollection`.

**Fix:** Register the missing service:
```csharp
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<IEmailService, EmailService>();
```

## "ITrain cannot be built: its constructor needs 'X', which is not registered"

The host refused to start because a train's own constructor asks for a type that nothing registers
in the container, so the train would fail every run. Register the type before building the host.
A type that *is* registered but can only be built inside a request (one reading `HttpContext`, say)
is not refused: the train is skipped with a warning, "was not verified at startup".

## "Junction 'X' (train 'Y') needs 'Z' as a constructor argument"

A junction's constructor asks for something the chain never produced and the container does not
hold. Trax builds a junction through its single public constructor, taking each argument from
Memory first and the container second, so the message names the junction, the train and the
missing type.

**Fix:** Register the missing service, or chain a junction that outputs it before this one. The
startup chain check does not verify constructor arguments, so this surfaces on the first run.

## "Junction 'X' has 2 public constructors"

Trax builds a junction through its single public constructor. A junction with more than one, or
none, cannot be built, so the startup chain check refuses the host naming the junction and the
count. Give it exactly one public constructor.

## Junction runs but Memory doesn't have the expected type

The chain couldn't find a type in Memory to pass to your junction. The run fails with
`Junction 'X' (train 'Y') needs 'Z' as its input, but nothing earlier in the chain produced one`.

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

## Train completes but metadata shows "Failed"

Check `FailureException` and `FailureReason` in the metadata record for details. `FailureJunction` identifies which junction threw, and `StackTrace` points to the original throw site. Common causes:
- An effect provider failed during `SaveChanges` (database connection, serialization error)
- A junction threw after the main train logic completed

If you catch the exception outside Trax, you'll see the original exception type and message. Structured junction context is available via `exception.Data["TrainExceptionData"]`.

## Junctions execute out of order or skip unexpectedly

If you're using `ShortCircuit`, remember that throwing an exception means "continue" not "stop." See [ShortCircuit](/docs/core/building-chains#shortcircuit) for details or [SDK Reference: ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) for all overloads.

## Scheduled jobs don't execute (no errors)

Possible causes:
- The manifest's `IsEnabled` is `false`. Check via `ITraxScheduler` or the database. A disabled manifest's already-queued entries also wait, `Queued`, until it is re-enabled. A restart does not re-enable a manifest or group that was disabled at runtime unless the code states `.Enabled(true)` (see [What a Restart Rewrites](/docs/scheduler/scheduling-options#what-a-restart-rewrites))
- A new cron schedule has not reached its first occurrence yet. It first runs at its first occurrence after it was scheduled, not on the next poll, and cron times are UTC: `Cron.Daily(hour: 3)` is 03:00 UTC. The manifest's `NextScheduledRun` shows when that is
- `ManifestManagerPollingInterval` or `JobDispatcherPollingInterval` is set too high and the job hasn't been picked up yet
- The train's input type doesn't implement `IManifestProperties`
- Your train assembly isn't registered with `AddMediator()`. Make sure to pass the assembly containing your trains

## `FormatException` or `ArgumentOutOfRangeException` from `Cron` or `Every`

`Schedule.FromCron`, which every `Cron` helper goes through, parses the expression and throws `FormatException` when it cannot fire: `Cron.Daily(hour: 25)`, `Cron.Hourly(minute: 60)`, a seven-field expression. `Schedule.FromInterval`, and so every `Every` helper, throws `ArgumentOutOfRangeException` for an interval shorter than one second, including `Every.Seconds(0)`. Fix the value at the call the stack trace names; earlier versions accepted these and stored a schedule that never ran, or ran once.

## "Manifest group 'X' is given two different MaxActiveJobs values"

Two schedules that share a group each state the same group setting (`MaxActiveJobs`, `Priority` or `Enabled`) with different values. Every start writes a stated group setting, so the value in force would depend on which manifest was seeded last, and `AddScheduler` refuses it. The message names both schedules.

**Fix:** state the setting on one member of the group, or the same value on each. A member that says only `.Group("name")` leaves the group's settings alone.

## "Batch 'X' prunes manifests whose external ID starts with ..."

One batch's prune prefix starts another batch's, so the first would delete the second's manifests at every start. A name-based `ScheduleMany(name, ...)` prunes only within its own group, so this is raised only when the groups do not keep the two apart: an explicit `PrunePrefix`, or two name-based batches moved into one group.

**Fix:** rename one batch so neither name plus `-` starts the other, or keep the batches in separate groups.

## "Ambiguous reference" between Cron types

Both Trax.Core and Hangfire define a `Cron` class. If you're importing both namespaces, the compiler can't tell which one you mean.

**Fix:** Use a namespace alias:
```csharp
using Cron = Trax.Scheduler.Services.Scheduling.Cron;
```

## "IManifestProperties" not found

`IManifestProperties` lives in the `Trax.Effect` package, not in the Scheduler package. Namespace: `Trax.Effect.Models.Manifest`.

**Fix:**
```csharp
using Trax.Effect.Models.Manifest;
```

## NuGet restore fails with NU1107 for Hangfire

The Scheduler.Hangfire package requires `Hangfire.Core >= 1.8` and `Hangfire.PostgreSql >= 1.20`. If your project pins an older version, NuGet can't resolve the dependency.

**Fix:** Update your Hangfire packages to match or exceed the minimum versions.

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect)
