---
layout: default
title: Configuration
parent: SDK Reference
nav_order: 2
has_children: true
---

# Configuration

All Trax services are configured through a single entry point: `AddTrax`. This method accepts a lambda that receives a `TraxBuilder`, which provides a fluent API for adding effects, the mediator, and the scheduler.

The builder uses a **step builder pattern** that enforces ordering at compile time. Each configuration step returns a different type that only exposes valid next steps:

1. `TraxBuilder` -- the initial builder, exposes `AddEffects()`
2. `TraxBuilderWithEffects` -- returned by `AddEffects()`, exposes `AddMediator()`
3. `TraxBuilderWithMediator` -- returned by `AddMediator()`, exposes `AddScheduler()`

This means you cannot call `AddMediator()` without first calling `AddEffects()`, and you cannot call `AddScheduler()` without first calling `AddMediator()`. The compiler catches incorrect ordering before you run your application.

Effect-specific methods are nested inside `.AddEffects(effects => ...)`, which receives a `TraxEffectBuilder`.

## Entry Point

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging()
        .AddJson()
        .SaveTrainParameters()
        .AddStepLogger()
        .AddStepProgress()
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseLocalWorkers()
        .MaxActiveJobs(10)
    )
);
```

## Signature

```csharp
public static IServiceCollection AddTrax(
    this IServiceCollection serviceCollection,
    Action<TraxBuilder>? options = null
)
```

Calling `AddTrax()` registers a `TraxMarker` singleton in the DI container. This marker is checked at runtime by `AddTraxDashboard()` and `AddTraxGraphQL()` -- if the marker is missing, they throw `InvalidOperationException` with a message directing you to call `AddTrax()` first.

## Step Builder Types

| Type | Returned By | Exposes |
|------|-------------|---------|
| `TraxBuilder` | `AddTrax()` lambda | `AddEffects()` |
| `TraxBuilderWithEffects` | `AddEffects()` | `AddMediator()` |
| `TraxBuilderWithMediator` | `AddMediator()`, `AddScheduler()` | `AddScheduler()` |

## Ordering Enforcement

The step builder pattern provides two levels of ordering enforcement:

### Compile-Time (Step Builder)

The fluent chain `AddEffects()` -> `AddMediator()` -> `AddScheduler()` is enforced by the type system. Each method returns a different builder type that only exposes valid next steps. If you try to call methods out of order, the code will not compile:

```csharp
// Compiles -- correct order
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler.UseLocalWorkers())
);

// Does NOT compile -- AddMediator() is not available on TraxBuilder
services.AddTrax(trax => trax
    .AddMediator(typeof(Program).Assembly)  // Error: TraxBuilder has no AddMediator
);

// Does NOT compile -- AddScheduler() is not available on TraxBuilderWithEffects
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddScheduler(scheduler => scheduler.UseLocalWorkers())  // Error: TraxBuilderWithEffects has no AddScheduler
);
```

### Runtime (TraxMarker)

`AddTraxDashboard()` and `AddTraxGraphQL()` are called on `IServiceCollection` / `WebApplicationBuilder`, not on the step builder chain. They perform a runtime check instead: if `AddTrax()` was not called first, they throw `InvalidOperationException` with a clear message:

```
InvalidOperationException: AddTrax() must be called before AddTraxDashboard().
Call services.AddTrax(...) in your service configuration before calling AddTraxDashboard().
```

## Builder Properties

These properties can be set directly on the `TraxEffectBuilder`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServiceCollection` | `IServiceCollection` | (from constructor) | Direct access to the DI container for manual registrations |
| `SerializeStepData` | `bool` | `false` | Whether step input/output data should be serialized globally |
| `LogLevel` | `LogLevel` | `LogLevel.Debug` | Minimum log level for effect logging |
| `TrainParameterJsonSerializerOptions` | `JsonSerializerOptions` | `TraxJsonSerializationOptions.Default` | System.Text.Json options for parameter serialization |
| `NewtonsoftJsonSerializerSettings` | `JsonSerializerSettings` | `TraxJsonSerializationOptions.NewtonsoftDefault` | Newtonsoft.Json settings for legacy serialization |

## Extension Methods

| Method | Description |
|--------|-------------|
| [UsePostgres]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}) | Adds PostgreSQL database support for metadata persistence |
| [UseInMemory]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %}) | Adds in-memory database support for testing/development |
| [AddDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}) | Enables logging for database operations |
| [AddJson]({{ site.baseurl }}{% link sdk-reference/configuration/add-json-effect.md %}) | Adds JSON change detection for tracking model mutations |
| [SaveTrainParameters]({{ site.baseurl }}{% link sdk-reference/configuration/save-train-parameters.md %}) | Serializes train input/output to JSON for persistence (optionally configurable) |
| [AddStepLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-logger.md %}) | Adds per-step execution logging |
| [AddStepProgress]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-progress.md %}) | Adds step progress tracking and cross-server cancellation checking |
| [AddMediator]({{ site.baseurl }}{% link sdk-reference/configuration/add-service-train-bus.md %}) | Registers the TrainBus and discovers trains via assembly scanning. Accepts `params Assembly[]` shorthand or `Action<TraxMediatorBuilder>` for full control (custom lifetime, multiple assemblies). Called on `TraxBuilderWithEffects`, returns `TraxBuilderWithMediator` |
| [AddEffect / AddStepEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect.md %}) | Registers custom effect provider factories |
| [AddLifecycleHook]({{ site.baseurl }}{% link sdk-reference/configuration/add-lifecycle-hook.md %}) | Registers lifecycle hooks that fire on train state transitions |
| [SetEffectLogLevel]({{ site.baseurl }}{% link sdk-reference/configuration/set-effect-log-level.md %}) | Sets the minimum log level for effect logging |
