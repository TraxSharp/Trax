---
layout: default
title: Builder Pattern
parent: Reference
nav_order: 9
---

# Builder Pattern

Four builders configure the subsystems: `TraxEffectBuilder`, `TraxMediatorBuilder`,
`SchedulerConfigurationBuilder` and `TraxGraphQLBuilder`. They agree on the class structure and
on the `Func<TBuilder, TBuilder>` entry point, and they disagree on what `Build()` returns and
what it is the caller's job to register. Match the closest one when you add a builder method;
match `TraxMediatorBuilder` when you add a whole builder, because it is the one that does every
part of this the intended way.

## Entry point

The extension method takes `Func<TBuilder, TBuilder>`, not `Action<TBuilder>`. It creates the
builder, invokes the function, calls `Build()`, and returns the next state marker in the chain.

```csharp
public static TraxBuilderWithEffects AddEffects(
    this TraxBuilder trax,
    Func<TraxEffectBuilder, TraxEffectBuilder> configure
)
```

`AddTrax` is the exception: it takes `Action<TraxBuilder>` because it is the root entry point
and returns `IServiceCollection` rather than a builder state type.

Provide a parameterless overload that calls the main one with an identity function.
`AddEffects()` and `AddScheduler()` have one. `AddMediator` does not: its second overload is
`params Assembly[]`, so `AddMediator()` compiles and reaches `Build()` with nothing to scan.
That is caught, not silent: `Build()` throws unless another subsystem contributed assemblies
first, in which case those are merged and scanned. A new builder should still prefer the
identity overload, so the mistake is unrepresentable rather than merely diagnosed.

## Class structure

Every builder is a `partial class` in its own directory, split one file per feature area:

| File | Holds |
|---|---|
| `Builder.cs` | State and constructor |
| `Builder.Build.cs` | Validation and the `Build()` method |
| `Builder.<Feature>.cs` | One feature area per file |

Methods return `this` for chaining. `TraxMediatorBuilder` and `TraxGraphQLBuilder` are the
reference implementations.

## Build()

`Build()` is `internal`, so the fluent chain is the only way to reach it. It holds the
build-time validation, which throws `InvalidOperationException` with a message that names the
misconfigured method, explains why it is wrong, and shows the fix. It never returns the parent
builder.

Three of the four return a configuration object and leave registration to the caller:
`TraxEffectBuilder.Build()` returns `TraxEffectConfiguration`, `TraxMediatorBuilder.Build()`
returns `MediatorConfiguration`, `TraxGraphQLBuilder.Build()` returns `GraphQLConfiguration`.

`SchedulerConfigurationBuilder.Build()` returns `void` and registers `SchedulerConfiguration`
and the scheduler's services itself, against the parent builder's `ServiceCollection`. That is
why `AddScheduler` returns the same `TraxBuilderWithMediator` it received rather than a new
marker: the scheduler is the last link in the chain, so there is no next state to promote to.
Do not copy the shape for a new subsystem. A `Build()` that returns the configuration keeps the
registration in the extension method where a reader looks for it.

## Configuration object

Named `{Subsystem}Configuration` and registered as a singleton, though where and how varies:

| Type | Setters | Registered by |
|---|---|---|
| `MediatorConfiguration` | `internal` | `AddMediator` |
| `SchedulerConfiguration` | `public` | `SchedulerConfigurationBuilder.Build()` |
| `GraphQLConfiguration` | none, assigned in the constructor | `AddTraxGraphQL` |
| `TraxEffectConfiguration` | `public` | `AddTrax`, as `ITraxEffectConfiguration` |

`MediatorConfiguration` is the shape to copy: internal setters make it immutable from outside
the builder, which is what stops a consumer mutating resolved configuration at run time.
`SchedulerConfiguration` is mutable after `Build()` on purpose, because
`SchedulerConfigBootstrapHostedService` overwrites the singleton at startup from the
`trax.scheduler_config` row the dashboard's server settings page writes.
`TraxEffectConfiguration` has public setters with no such reason behind them.
`GraphQLConfiguration` goes the other way and takes everything through its constructor.

## State markers

`TraxBuilder` to `TraxBuilderWithEffects` to `TraxBuilderWithMediator`. All three expose
`ServiceCollection`, `HasDatabaseProvider` and `HasDataProvider`. `Root` is not part of that
set: `TraxBuilder` is the root and has none, `TraxBuilderWithMediator.Root` is `internal`, and
only `TraxBuilderWithEffects.Root` is public, because `AddMediator` needs it to read the
assemblies earlier subsystems contributed.

Extension methods target a specific marker, which is what enforces ordering at compile time
rather than at run time. `AddDataContextLogging()` is the same idea one level down: it is
declared on `TraxEffectBuilderWithData`, which only a data provider call returns.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus)
