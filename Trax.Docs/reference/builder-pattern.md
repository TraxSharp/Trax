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
| `SchedulerConfiguration` | mostly `internal`, a few hidden `public` | `SchedulerConfigurationBuilder.Build()` |
| `GraphQLConfiguration` | none, assigned in an `internal` constructor | `AddTraxGraphQL` |
| `TraxEffectConfiguration` | `public` | `AddTrax`, as `ITraxEffectConfiguration` |

`MediatorConfiguration` is the shape to copy: internal setters make it immutable from outside
the builder, which is what stops a consumer mutating resolved configuration at run time.
`SchedulerConfiguration` mostly follows it. Runtime changes go through the validated
`operations.config` path that the dashboard's server settings page also uses, and the scheduler
re-applies the `trax.scheduler_config` row that path writes to the singleton at startup and every few seconds while it runs. A few
setters that existing hosts and tests assign directly (the polling intervals, `MaxActiveJobs`,
`DefaultMaxRetries`, `DefaultRetryDelay`, `DefaultJobTimeout`, `ManifestManagerEnabled`) stay public but are hidden from
IntelliSense.
`TraxEffectConfiguration` has public setters with no reason behind them.
`GraphQLConfiguration` goes the other way and takes everything through its constructor, which is
`internal`: only `TraxGraphQLBuilder.Build()` creates one.

## State markers

`TraxBuilder` to `TraxBuilderWithEffects` to `TraxBuilderWithMediator`. All three expose
`ServiceCollection`, `HasDatabaseProvider` and `HasDataProvider`. `Root` is not part of that
set: `TraxBuilder` is the root and has none, `TraxBuilderWithMediator.Root` is `internal`, and
only `TraxBuilderWithEffects.Root` is public, because `AddMediator` needs it to read the
assemblies earlier subsystems contributed.

`HasDatabaseProvider`, `HasDataProvider` and `TraxBuilder.MediatorConfigured` are read-only
outside Trax. Build-time validation reads them to fail closed (the scheduler refuses to build
without a data provider, `AddStateMachines` refuses to run after `AddMediator`), so only the call
that earns a flag sets it: a data provider's `Use*` method, or `AddMediator`. The setters are
`internal`, visible to the Trax.Effect data provider assemblies and Trax.Mediator. A test that
needs a builder with a data provider calls a real one, `UseInMemory()` being the cheapest.

Extension methods target a specific marker, which is what enforces ordering at compile time
rather than at run time. `AddDataContextLogging()` is the same idea one level down: it is
declared on `TraxEffectBuilderWithData`, which only a data provider call returns.

### Wrong-order overloads

A call made on the wrong marker would otherwise fail with CS1929, which names the marker types
and leaves the reader to work out the order from them. So a transition someone can get wrong also
gets an overload on the marker the call is wrongly made on, marked
`[Obsolete("Call X(...) before Y(...).", error: true)]` and `[EditorBrowsable(Never)]`. The
compiler then reports CS0619 with the instruction as its text:

```
error CS0619: 'BuilderOrderExtensions.AddMediator(TraxBuilder, params Assembly[])' is obsolete:
'Call AddEffects(...) before AddMediator(...).'
```

Trax.Mediator's are in `BuilderOrderExtensions`:

| Wrong call | Error text |
|---|---|
| `AddMediator(...)` on `TraxBuilder`, before `AddEffects` | `Call AddEffects(...) before AddMediator(...).` |
| `AddMediator(...)` on `TraxBuilderWithMediator`, a second time | `AddMediator(...) is already called. Call it once and configure everything in that call.` |
| `AddStateMachines(...)` on `TraxBuilderWithMediator`, after `AddMediator` | `Call AddStateMachines(...) before AddMediator(...).` |

Trax.Effect.Data's is in its own `BuilderOrderExtensions`:

| Wrong call | Error text |
|---|---|
| `AddDataContextLogging(...)` on `TraxEffectBuilder`, before a data provider | `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).` |

When you add one, take the real method's parameter list and names exactly, so a call that uses
named arguments or a lambda binds to the overload and reports the instruction rather than an
argument error. The receiver has to be a marker the real method does not accept, or the correct
order becomes ambiguous. The overload lives in the package that owns the later marker, because
that is the one that can name both types: `AddStateMachines` is Trax.Effect's, but the
`TraxBuilderWithMediator` overload is Trax.Mediator's, and its options parameter is
`Action<dynamic>` because Trax.Mediator does not reference the options type. The body throws and
never runs. `BuilderOrderDiagnosticsTests`, in Trax.Mediator and in Trax.Effect, compiles each wrong
order in memory and asserts the one error it produces, and compiles each right order and asserts none.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus)
