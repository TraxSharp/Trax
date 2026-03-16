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
- The assembly containing your train wasn't registered with `AddMediator`
- Your train doesn't implement `IServiceTrain<TIn, TOut>`
- Your train class is `abstract`

**Fix:**
```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(YourTrain).Assembly)  // Ensure correct assembly
);
```

*SDK Reference: [AddMediator]({{ site.baseurl }}{% link sdk-reference/configuration/add-service-train-bus.md %})*

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

## Compile error: "TraxBuilder does not contain a definition for AddMediator"

The step builder pattern enforces configuration ordering at compile time. `AddMediator()` is only available on `TraxBuilderWithEffects` (returned by `AddEffects()`), and `AddScheduler()` is only available on `TraxBuilderWithMediator` (returned by `AddMediator()`).

**Cause:** Calling methods out of order, e.g. `AddMediator()` before `AddEffects()`.

**Fix:** Follow the required order: `AddEffects()` -> `AddMediator()` -> `AddScheduler()`:
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

## Junction runs but Memory doesn't have the expected type

The chain couldn't find a type in Memory to pass to your junction.

**Causes:**
- A previous junction didn't return or add the expected type to Memory
- Type mismatch between junction output and next junction's input

**Fix:** Check the chain flow. Each junction's input type must exist in Memory (either from `Activate()` or a previous junction's output):
```csharp
Activate(input)                    // Memory: CreateUserRequest
    .Chain<ValidateJunction>()         // Takes CreateUserRequest, returns Unit
    .Chain<CreateUserJunction>()       // Takes CreateUserRequest, returns User
    .Chain<SendEmailJunction>()        // Takes User (from previous junction)
    .Resolve();
```

The [Analyzer](../core/analyzer.md) catches most of these issues at compile time—if you see CHAIN001, the message tells you exactly which type is missing and what's available.

## Train completes but metadata shows "Failed"

Check `FailureException` and `FailureReason` in the metadata record for details. Common causes:
- An effect provider failed during `SaveChanges` (database connection, serialization error)
- A junction threw after the main train logic completed

## Junctions execute out of order or skip unexpectedly

If you're using `ShortCircuit`, remember that throwing an exception means "continue" not "stop." See [ShortCircuit](short-circuit.md) for details or [SDK Reference: ShortCircuit]({{ site.baseurl }}{% link sdk-reference/train-methods/short-circuit.md %}) for all overloads.

## Scheduled jobs don't execute (no errors)

Possible causes:
- The manifest's `IsEnabled` is `false`—check via `ITraxScheduler` or the database
- `ManifestManagerPollingInterval` or `JobDispatcherPollingInterval` is set too high and the job hasn't been picked up yet
- The train's input type doesn't implement `IManifestProperties`
- Your train assembly isn't registered with `AddMediator()` — make sure to pass the assembly containing your trains

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
