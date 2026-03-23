---
layout: default
title: Analyzer
parent: Core
nav_order: 4
---

# Analyzer

Trax.Core includes a Roslyn analyzer that validates your train's route at compile time — like a route planner that checks every junction has the cargo it needs before the train ever departs. When you chain junctions via `.Chain<TJunction>()`, the analyzer simulates the runtime Memory dictionary to verify that each junction's input type is available before that junction executes.

## The Problem

Consider this train:

```csharp
Chain<LoadMetadataJunction>()                // TIn=RunJobRequest -> TOut=Metadata
    .Chain<ValidateMetadataStateJunction>()  // TIn=Metadata -> TOut=Unit
    .Chain<RunScheduledTrainJunction>()
    .Chain<UpdateManifestSuccessJunction>();
```

If someone removes `LoadMetadataJunction`, `ValidateMetadataStateJunction` expects `Metadata` in Memory but nothing produces it. Today this is a runtime error — the train fails when it tries to find `Metadata` in the dictionary. You won't discover this until the code actually runs.

The analyzer makes it a compile-time error. You see the problem immediately in your IDE, before you even build.

## What It Checks

The analyzer triggers on `Junctions()` overrides and `.Resolve()` calls in `Train<,>` or `ServiceTrain<,>` subclasses. It walks through the chain and simulates Memory forward:

```
Junctions()           -> Memory = { TInput, Unit }
Chain<JunctionA>()    -> Check: is JunctionA's TIn in Memory? Add JunctionA's TOut.
.Chain<JunctionB>()   -> Check: is JunctionB's TIn in Memory? Add JunctionB's TOut.
                      -> Check: is TReturn in Memory?
```

| Method | What the analyzer does |
|--------|----------------------|
| `Junctions()` / `Activate(input)` | Seeds Memory with `TInput` and `Unit` |
| `.Chain<TJunction>()` | Checks `TIn` in Memory, then adds `TOut` |
| `.ShortCircuit<TJunction>()` | Same as `Chain` — checks `TIn` in Memory, adds `TOut` |
| `.AddServices<T1, T2>()` | Adds each type argument to Memory |
| `.Extract<TIn, TOut>()` | Adds `TOut` to Memory |
| `.Resolve()` / end of chain | Checks `TReturn` in Memory |

## Diagnostics

### CHAIN001: Junction input type not available (Error)

Fires when a junction needs a type that no previous junction has produced.

```csharp
public class BrokenTrain : ServiceTrain<string, Unit>
{
    protected override Unit Junctions() =>
        Chain<LogGreetingJunction>();  // <- CHAIN001: LogGreetingJunction requires HelloWorldInput,
                                      //   but Memory only has [string, Unit]
}
```

The message tells you exactly what's missing and what's available:

```
error CHAIN001: Junction 'LogGreetingJunction' requires input type 'HelloWorldInput'
which has not been produced by a previous junction. Available: [string, Unit].
```

### CHAIN002: Train return type not available (Error)

Fires when `Resolve()` needs a type that hasn't been produced. The analyzer tracks all chain methods including `ShortCircuit`, so a missing return type is always an error.

```csharp
public class MissingReturnTrain : ServiceTrain<OrderRequest, Receipt>
{
    protected override Receipt Junctions() =>
        Chain<ValidateOrderJunction>();  // Returns Unit
                                         // <- CHAIN002: Receipt not in Memory
}
```

## Tuple and Interface Handling

The analyzer mirrors the runtime's Memory behavior:

**Tuple outputs are decomposed.** When a junction produces `(User, Order)`, the analyzer adds `User` and `Order` to Memory individually (not the tuple itself). This matches how the runtime stores tuple elements.

**Tuple inputs are validated component-by-component.** When a junction takes `(User, Order)`, the analyzer checks that both `User` and `Order` are individually available in Memory.

**Interface resolution works through concrete types.** When a junction produces `ConcreteUser` (which implements `IUser`), the analyzer adds both `ConcreteUser` and `IUser` to Memory. A subsequent junction requiring `IUser` will pass validation.

## Known Limitations

**Sibling interface inputs.** When the train's `TInput` is an interface (e.g., `Train<IFoo, Unit>`) and a junction requires a different interface that the runtime concrete type also implements, the analyzer can't verify this. Suppress with `#pragma warning disable CHAIN001`.

**Cross-method chains.** The analyzer only looks within a single method body. If you build a chain across helper methods, it won't follow the calls.

## Setup

The analyzer ships with the Trax.Core NuGet package. If you're referencing Trax.Core, you already have it — no additional setup required.

For development within the Trax.Core solution itself, the analyzer is propagated to all projects via `Directory.Build.props`:

```xml
<ItemGroup Condition="'$(MSBuildProjectName)' != 'Trax.Core.Analyzers'">
    <ProjectReference Include="$(MSBuildThisFileDirectory)src/Trax.Core.Analyzers/Trax.Core.Analyzers.csproj"
                      ReferenceOutputAssembly="false"
                      OutputItemType="Analyzer" />
</ItemGroup>
```

## Suppressing Diagnostics

If the analyzer fires on a chain that you know is correct (interface patterns, dynamic Memory seeding, etc.), suppress it with a pragma:

```csharp
#pragma warning disable CHAIN001
    .Chain<MyDynamicJunction>()
#pragma warning restore CHAIN001
```

Or suppress at the project level in your `.csproj`:

```xml
<PropertyGroup>
    <NoWarn>$(NoWarn);CHAIN001</NoWarn>
</PropertyGroup>
```

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | [Extract](/docs/sdk-reference/train-methods/extract) | [AddServices](/docs/sdk-reference/train-methods/add-services) | [Activate](/docs/sdk-reference/train-methods/activate) | [Resolve](/docs/sdk-reference/train-methods/resolve)
