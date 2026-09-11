---
layout: default
title: Builder Pattern
parent: Reference
nav_order: 9
---

# Builder Pattern

Every Trax subsystem builder (Effect, Mediator, Scheduler) follows the same shape. This is
the convention to match when you add a builder method or a new builder.

## Entry point

The extension method takes `Func<TBuilder, TBuilder>`, not `Action<TBuilder>`. It creates the
builder, invokes the function, calls `Build()`, registers the result, and returns the next
state marker in the chain. Provide a parameterless overload that calls the main one with an
identity function.

`AddTrax` is the exception: it takes `Action<TraxBuilder>` because it is the root entry point
and returns no builder state type.

```csharp
public static TraxBuilderWithEffects AddEffects(
    this TraxBuilder trax,
    Func<TraxEffectBuilder, TraxEffectBuilder> configure
)
```

## Class structure

A builder is always a `partial class` in its own directory, split one file per feature area:

| File | Holds |
|---|---|
| `Builder.cs` | State and constructor |
| `Builder.Build.cs` | Validation and the `Build()` method |
| `Builder.<Feature>.cs` | One feature area per file |

Methods return `this` for chaining. `TraxMediatorBuilder` and `TraxGraphQLBuilder` are the
reference implementations.

## Build()

`Build()` returns a configuration object or `void`. It never returns the parent builder. It
holds the build-time validation, which throws `InvalidOperationException` with a message that
names the misconfigured method, explains why it is wrong, and shows the fix.

## Configuration object

Named `{Subsystem}Configuration`, with internal setters so it is immutable from outside the
builder, and registered as a singleton.

## State markers

`TraxBuilder` to `TraxBuilderWithEffects` to `TraxBuilderWithMediator`. Each exposes `Root`,
`ServiceCollection`, `HasDatabaseProvider` and `HasDataProvider`. Extension methods target a
specific marker, which is what enforces ordering at compile time rather than at run time.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddServiceTrainBus](/docs/sdk-reference/mediator-api/add-service-train-bus)
