---
title: AddEffects
description: "Reference for AddEffects, the TraxEffectBuilder and TraxEffectBuilderWithData it hands out, their properties, and the builder methods each one carries."
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 2
package: Trax.Effect
namespace:
  - Trax.Effect.Extensions
  - Trax.Effect.Configuration.TraxEffectBuilder
  - Trax.Effect.Configuration.TraxEffectConfiguration
source:
  - Trax.Effect/src/Trax.Effect/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxEffectBuilder/TraxEffectBuilder.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxEffectBuilder/TraxEffectBuilderWithData.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxEffectConfiguration/ITraxEffectConfiguration.cs
  - Trax.Effect/src/Trax.Effect/Configuration/TraxEffectConfiguration/TraxEffectConfiguration.cs
---

# AddEffects

The first step on the [AddTrax](/docs/sdk-reference/configuration/add-trax) builder. Configures the effect system:
the data provider, the effects that run around each train and junction, lifecycle hooks, broadcasting and deciders.
The callback receives a `TraxEffectBuilder` and returns the builder from its last chained call.

## Signatures

```csharp fragment
public static TraxBuilderWithEffects AddEffects(
    this TraxBuilder builder,
    Func<TraxEffectBuilder, TraxEffectBuilder> configure
)

public static TraxBuilderWithEffects AddEffects(
    this TraxBuilder builder
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Func<TraxEffectBuilder, TraxEffectBuilder>` | Chains the effect methods and returns the builder from the last call. The parameterless overload is `AddEffects(effects => effects)`. |

## Returns

`TraxBuilderWithEffects`, on which [AddMediator](/docs/sdk-reference/configuration/add-mediator) is the next step.

## Behavior

`configure` runs once. The builder it returns is built into the `ITraxEffectConfiguration` that
[AddTrax](/docs/sdk-reference/configuration/add-trax) registers. With no data provider configured, the methods that
need one fail when the configuration is built: `AddJunctionProgress()` throws `InvalidOperationException` from
`AddEffects`, and `AddScheduler()` throws `InvalidOperationException` naming the three providers.

### TraxEffectBuilder

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServiceCollection` | `IServiceCollection` | | The host's service collection, for registrations of your own |
| `EffectRegistry` | `IEffectRegistry?` | | The registry effects are registered in |
| `HasDataProvider` | `bool` | `false` | True once a data provider method ran. Set only by those methods. |
| `HasDatabaseProvider` | `bool` | `false` | True once `UsePostgres` or `UseSqlite` ran. Set only by those methods. |
| `MigrationsDisabled` | `bool` | `false` | Set by [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) |
| `JunctionProgressEnabled` | `bool` | `false` | Set by [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress); building the configuration then requires a data provider |
| `SerializeJunctionData` | `bool` | `false` | Set by `AddJunctionLogger(serializeJunctionData:)` |
| `LogLevel` | `LogLevel` | `LogLevel.Debug` | Set by [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level) |
| `TrainParameterJsonSerializerOptions` | `JsonSerializerOptions` | `TraxJsonSerializationOptions.Default` | Replaced by [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) when it is given options |

All but `HasDataProvider` and `HasDatabaseProvider` are hidden from completion: the builder methods set them.

### TraxEffectBuilderWithData

`UsePostgres`, `UseSqlite` and `UseInMemory` return `TraxEffectBuilderWithData`, a subclass of `TraxEffectBuilder`
that carries the state set before it. The methods that need a data provider extend only this type. Called on a
plain `TraxEffectBuilder`, each binds to an overload marked obsolete as an error, so it fails to compile with the
instruction, for example `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).`

The generic methods (`TBuilder : TraxEffectBuilder`) return the builder type they were called on, so a chain that
starts with a data provider keeps `TraxEffectBuilderWithData` through them.

| Method | Extends | Package |
|--------|---------|---------|
| [UsePostgres](/docs/sdk-reference/configuration/use-postgres), [SkipMigrations](/docs/sdk-reference/configuration/skip-migrations) | `TraxEffectBuilder` | `Trax.Effect.Data.Postgres` |
| [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | `TraxEffectBuilder` | `Trax.Effect.Data.Sqlite` |
| [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | `TraxEffectBuilder` | `Trax.Effect.Data.InMemory` |
| [AddDataContextLogging](/docs/sdk-reference/configuration/add-data-context-logging), [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording), [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | `TraxEffectBuilderWithData` | `Trax.Effect.Data` |
| [AddEffect, AddJunctionEffect](/docs/sdk-reference/configuration/add-effect), [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | `TraxEffectBuilder` | `Trax.Effect` |
| [SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level), [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | `TBuilder` | `Trax.Effect` |
| [AddJson](/docs/sdk-reference/configuration/add-json) | `TBuilder` | `Trax.Effect.Provider.Json` |
| [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | `TBuilder` | `Trax.Effect.Provider.Parameter` |
| [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | `TBuilder` | `Trax.Effect.JunctionProvider.Logging` |
| [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | `TBuilder` | `Trax.Effect.JunctionProvider.Progress` |
| [AddNimbleDecider](/docs/sdk-reference/configuration/add-nimble-decider), [AddSystemOneDecider](/docs/sdk-reference/configuration/add-system-one-decider) | `TBuilder` | `Trax.Effect.Decisions.SystemOne` |

`AddEffect`, `AddJunctionEffect` and `AddLifecycleHook` return `TraxEffectBuilder`, so a chain that needs
`TraxEffectBuilderWithData` afterwards calls them before the data provider.

### ITraxEffectConfiguration

The built configuration, registered as a singleton.

| Member | Description |
|--------|-------------|
| `LogLevel` | The builder's `LogLevel` |
| `SerializeJunctionData` | The builder's `SerializeJunctionData` |
| `SystemJsonSerializerOptions` | The builder's `TrainParameterJsonSerializerOptions` |
| `TraxEffectConfiguration.StaticSystemJsonSerializerOptions` | Static. The same options, set process-wide when the configuration is built |

## Examples

An expression lambda returns its last call:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddDataContextLogging()
        .AddJson()
        .SaveTrainParameters()
        .AddJunctionLogger()
        .AddJunctionProgress()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

A block lambda returns the builder itself:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects =>
    {
        effects.ServiceCollection.AddSingleton(TimeProvider.System);
        return effects.UsePostgres(connectionString).AddJson();
    })
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [AddTrax](/docs/sdk-reference/configuration/add-trax), [AddMediator](/docs/sdk-reference/configuration/add-mediator)
- [Effect Providers](/docs/effect/effect-providers), what the effects are for
