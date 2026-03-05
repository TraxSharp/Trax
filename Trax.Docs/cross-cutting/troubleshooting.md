---
layout: default
title: Troubleshooting
parent: Cross-Cutting
nav_order: 4
---

# Troubleshooting

## "No train found for input type X"

The `TrainBus` couldn't find a train that accepts your input type.

**Causes:**
- The assembly containing your train wasn't registered with `AddServiceTrainBus`
- Your train doesn't implement `IServiceTrain<TIn, TOut>`
- Your train class is `abstract`

**Fix:**
```csharp
services.AddTrax.CoreEffects(o =>
    o.AddServiceTrainBus(typeof(YourTrain).Assembly)  // Ensure correct assembly
);
```

*SDK Reference: [AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-service-train-bus.md %})*

## "Unable to resolve service for type 'IStep'"

A step's dependency isn't registered in the DI container.

**Cause:** Your step injects a service that wasn't added to `IServiceCollection`.

**Fix:** Register the missing service:
```csharp
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<IEmailService, EmailService>();
```

## Step runs but Memory doesn't have the expected type

The chain couldn't find a type in Memory to pass to your step.

**Causes:**
- A previous step didn't return or add the expected type to Memory
- Type mismatch between step output and next step's input

**Fix:** Check the chain flow. Each step's input type must exist in Memory (either from `Activate()` or a previous step's output):
```csharp
Activate(input)                    // Memory: CreateUserRequest
    .Chain<ValidateStep>()         // Takes CreateUserRequest, returns Unit
    .Chain<CreateUserStep>()       // Takes CreateUserRequest, returns User
    .Chain<SendEmailStep>()        // Takes User (from previous step)
    .Resolve();
```

The [Analyzer](../core/analyzer.md) catches most of these issues at compile time—if you see CHAIN001, the message tells you exactly which type is missing and what's available.

## Train completes but metadata shows "Failed"

Check `FailureException` and `FailureReason` in the metadata record for details. Common causes:
- An effect provider failed during `SaveChanges` (database connection, serialization error)
- A step threw after the main train logic completed

## Steps execute out of order or skip unexpectedly

If you're using `ShortCircuit`, remember that throwing an exception means "continue" not "stop." See [ShortCircuit](short-circuit.md) for details or [SDK Reference: ShortCircuit]({{ site.baseurl }}{% link sdk-reference/train-methods/short-circuit.md %}) for all overloads.

## Scheduled jobs don't execute (no errors)

The most common cause: `TaskServerExecutorTrain.Assembly` isn't registered with the `TrainBus`. The ManifestManager enqueues jobs to Hangfire, but Hangfire can't resolve the executor train.

**Fix:**
```csharp
.AddServiceTrainBus(
    typeof(Program).Assembly,
    typeof(TaskServerExecutorTrain).Assembly  // Don't forget this
)
```

Other causes:
- The manifest's `IsEnabled` is `false`—check via `ITraxScheduler` or the database
- `ManifestManagerPollingInterval` or `JobDispatcherPollingInterval` is set too high and the job hasn't been picked up yet
- The train's input type doesn't implement `IManifestProperties`

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
