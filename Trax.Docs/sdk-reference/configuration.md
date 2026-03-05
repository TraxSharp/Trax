---
layout: default
title: Configuration
parent: SDK Reference
nav_order: 2
has_children: true
---

# Configuration

All Trax.Core Effect services are configured through a single entry point: `AddTrax.CoreEffects`. This method accepts a lambda that receives a `Trax.CoreEffectConfigurationBuilder`, which provides a fluent API for adding data providers, effect providers, and orchestration services.

## Entry Point

```csharp
services.AddTrax.CoreEffects(options => options
    .AddPostgresEffect(connectionString)
    .AddEffectDataContextLogging()
    .AddJsonEffect()
    .SaveTrainParameters()
    .AddStepLogger()
    .AddStepProgress()
    .AddServiceTrainBus(assemblies: typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseHangfire(connectionString)
        .MaxActiveJobs(10)
    )
);
```

## Signature

```csharp
public static IServiceCollection AddTrax.CoreEffects(
    this IServiceCollection serviceCollection,
    Action<Trax.CoreEffectConfigurationBuilder>? options = null
)
```

## Builder Properties

These properties can be set directly on the `Trax.CoreEffectConfigurationBuilder`:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServiceCollection` | `IServiceCollection` | (from constructor) | Direct access to the DI container for manual registrations |
| `SerializeStepData` | `bool` | `false` | Whether step input/output data should be serialized globally |
| `LogLevel` | `LogLevel` | `LogLevel.Debug` | Minimum log level for effect logging |
| `TrainParameterJsonSerializerOptions` | `JsonSerializerOptions` | `Trax.CoreJsonSerializationOptions.Default` | System.Text.Json options for parameter serialization |
| `NewtonsoftJsonSerializerSettings` | `JsonSerializerSettings` | `Trax.CoreJsonSerializationOptions.NewtonsoftDefault` | Newtonsoft.Json settings for legacy serialization |

## Extension Methods

| Method | Description |
|--------|-------------|
| [AddPostgresEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-postgres-effect.md %}) | Adds PostgreSQL database support for metadata persistence |
| [AddInMemoryEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-in-memory-effect.md %}) | Adds in-memory database support for testing/development |
| [AddEffectDataContextLogging]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-data-context-logging.md %}) | Enables logging for database operations |
| [AddJsonEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-json-effect.md %}) | Adds JSON change detection for tracking model mutations |
| [SaveTrainParameters]({{ site.baseurl }}{% link sdk-reference/configuration/save-train-parameters.md %}) | Serializes train input/output to JSON for persistence (optionally configurable) |
| [AddStepLogger]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-logger.md %}) | Adds per-step execution logging |
| [AddStepProgress]({{ site.baseurl }}{% link sdk-reference/configuration/add-step-progress.md %}) | Adds step progress tracking and cross-server cancellation checking |
| [AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-service-train-bus.md %}) | Registers the TrainBus and discovers trains via assembly scanning |
| [AddEffect / AddStepEffect]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect.md %}) | Registers custom effect provider factories |
| [AddLifecycleHook]({{ site.baseurl }}{% link sdk-reference/configuration/add-lifecycle-hook.md %}) | Registers lifecycle hooks that fire on train state transitions |
| [SetEffectLogLevel]({{ site.baseurl }}{% link sdk-reference/configuration/set-effect-log-level.md %}) | Sets the minimum log level for effect logging |
