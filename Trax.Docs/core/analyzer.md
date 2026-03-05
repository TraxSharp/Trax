---
layout: default
title: Analyzer
parent: Core
nav_order: 4
---

# Analyzer

Trax.Core includes a Roslyn analyzer that validates your train's route at compile time — like a route planner that checks every stop has the cargo it needs before the train ever departs. When you chain steps via `.Chain<TStep>()`, the analyzer simulates the runtime Memory dictionary to verify that each step's input type is available before that step executes.

## The Problem

Consider this train:

```csharp
Activate(input)                          // input: ExecuteManifestRequest
    .Chain<LoadMetadataStep>()           // TIn=ExecuteManifestRequest -> TOut=Metadata
    .Chain<ValidateMetadataStateStep>()  // TIn=Metadata -> TOut=Unit
    .Chain<ExecuteScheduledTrainStep>()
    .Chain<UpdateManifestSuccessStep>()
    .Resolve();
```

If someone removes `LoadMetadataStep`, `ValidateMetadataStateStep` expects `Metadata` in Memory but nothing produces it. Today this is a runtime error — the train fails when it tries to find `Metadata` in the dictionary. You won't discover this until the code actually runs.

The analyzer makes it a compile-time error. You see the problem immediately in your IDE, before you even build.

## What It Checks

The analyzer triggers on every `.Resolve()` call in a `Train<,>` or `ServiceTrain<,>` subclass. It walks backward through the fluent chain to `Activate()`, then simulates Memory forward:

```
Activate(input)       -> Memory = { TInput, Unit }
.Chain<StepA>()       -> Check: is StepA's TIn in Memory? Add StepA's TOut.
.Chain<StepB>()       -> Check: is StepB's TIn in Memory? Add StepB's TOut.
.Resolve()            -> Check: is TReturn in Memory?
```

| Method | What the analyzer does |
|--------|----------------------|
| `Activate(input)` | Seeds Memory with `TInput` and `Unit` |
| `.Chain<TStep>()` | Checks `TIn` in Memory, then adds `TOut` |
| `.ShortCircuit<TStep>()` | Same as `Chain` — checks `TIn` in Memory, adds `TOut` |
| `.AddServices<T1, T2>()` | Adds each type argument to Memory |
| `.Extract<TIn, TOut>()` | Adds `TOut` to Memory |
| `.Resolve()` | Checks `TReturn` in Memory |

## Diagnostics

### CHAIN001: Step input type not available (Error)

Fires when a step needs a type that no previous step has produced.

```csharp
public class BrokenTrain : ServiceTrain<string, Unit>
{
    protected override async Task<Either<Exception, Unit>> RunInternal(string input) =>
        Activate(input)
            .Chain<LogGreetingStep>()  // <- CHAIN001: LogGreetingStep requires HelloWorldInput,
            .Resolve();               //   but Memory only has [string, Unit]
}
```

The message tells you exactly what's missing and what's available:

```
error CHAIN001: Step 'LogGreetingStep' requires input type 'HelloWorldInput'
which has not been produced by a previous step. Available: [string, Unit].
```

### CHAIN002: Train return type not available (Error)

Fires when `Resolve()` needs a type that hasn't been produced. The analyzer tracks all chain methods including `ShortCircuit`, so a missing return type is always an error.

```csharp
public class MissingReturnTrain : ServiceTrain<OrderRequest, Receipt>
{
    protected override async Task<Either<Exception, Receipt>> RunInternal(OrderRequest input) =>
        Activate(input)
            .Chain<ValidateOrderStep>()  // Returns Unit
            .Resolve();                  // <- CHAIN002: Receipt not in Memory
}
```

## Tuple and Interface Handling

The analyzer mirrors the runtime's Memory behavior:

**Tuple outputs are decomposed.** When a step produces `(User, Order)`, the analyzer adds `User` and `Order` to Memory individually (not the tuple itself). This matches how the runtime stores tuple elements.

**Tuple inputs are validated component-by-component.** When a step takes `(User, Order)`, the analyzer checks that both `User` and `Order` are individually available in Memory.

**Interface resolution works through concrete types.** When a step produces `ConcreteUser` (which implements `IUser`), the analyzer adds both `ConcreteUser` and `IUser` to Memory. A subsequent step requiring `IUser` will pass validation.

## Known Limitations

**Sibling interface inputs.** When the train's `TInput` is an interface (e.g., `Train<IFoo, Unit>`) and a step requires a different interface that the runtime concrete type also implements, the analyzer can't verify this. Suppress with `#pragma warning disable CHAIN001`.

**Cross-method chains.** The analyzer only looks within a single method body. If you build a chain across helper methods, it won't follow the calls.

## Setup

The analyzer ships with the Trax.Core NuGet package. If you're referencing Trax.Core, you already have it — no additional setup required.

For development within the Trax.Core solution itself, the analyzer is propagated to all projects via `Directory.Build.props`:

```xml
<ItemGroup Condition="'$(MSBuildProjectName)' != 'Trax.Core.Analyzers'">
    <ProjectReference Include="$(MSBuildThisFileDirectory)src/analyzers/Trax.Core.Analyzers/Trax.Core.Analyzers.csproj"
                      ReferenceOutputAssembly="false"
                      OutputItemType="Analyzer" />
</ItemGroup>
```

## Suppressing Diagnostics

If the analyzer fires on a chain that you know is correct (interface patterns, dynamic Memory seeding, etc.), suppress it with a pragma:

```csharp
#pragma warning disable CHAIN001
    .Chain<MyDynamicStep>()
#pragma warning restore CHAIN001
```

Or suppress at the project level in your `.csproj`:

```xml
<PropertyGroup>
    <NoWarn>$(NoWarn);CHAIN001</NoWarn>
</PropertyGroup>
```
