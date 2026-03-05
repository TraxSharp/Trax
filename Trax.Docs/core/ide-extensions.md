---
layout: default
title: IDE Extensions
parent: Core
nav_order: 5
---

# IDE Extensions

Inlay hint extensions for **VSCode** and **Rider/ReSharper**. Both show `TIn -> TOut` types inline for each chain call.

## What They Show

Given a train chain:

```csharp
protected override async Task<Either<Exception, OrderReceipt>> RunInternal(OrderRequest input)
    => Activate(input)
        .Chain<CheckInventoryStep>()
        .Chain<ChargePaymentStep>()
        .Chain<CreateShipmentStep>()
        .Resolve();
```

The extensions annotate each step with its resolved types:

```
.Chain<CheckInventoryStep>()      // OrderRequest -> InventoryResult
.Chain<ChargePaymentStep>()       // InventoryResult -> PaymentConfirmation
.Chain<CreateShipmentStep>()      // PaymentConfirmation -> OrderReceipt
```

### Supported Methods

- `.Chain<TStep>()`
- `.ShortCircuit<TStep>()`

### Type Resolution

Types are resolved by walking the step's type hierarchy to find `IStep<TIn, TOut>`. This handles:
- Direct inheritance: `class MyStep : Step<string, int>`
- Primary constructors: `class MyStep(ILogger logger) : EffectStep<string, int>`
- Nested generics: `EffectStep<Unit, List<Manifest>>`
- Tuple types: `EffectStep<(List<A>, List<B>), List<C>>`

## VSCode

**Source:** `plugins/vscode/trax-hints/`

### Installation

Install from the [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints)

### Internals

Activates on C# files. Uses regex to find chain calls, then calls VSCode's definition provider to jump to each step's source file and extract the generic arguments from the class definition.

Resolved types are cached per step class. The cache clears when any `.cs` file is modified.

## Rider / ReSharper

**Source:** `plugins/rider-resharper/`

### Installation

Search for **Trax.Core Chain Hints** in JetBrains Marketplace.

Or build from source:

```bash
cd plugins/rider-resharper
./gradlew buildPlugin
```

Output is in `build/distributions/`.

### Internals

Uses JetBrains' PSI (Program Structure Interface) for type resolution. A daemon analyzer (`ElementProblemAnalyzer<IInvocationExpression>`) fires on each invocation expression and:

1. Checks if the method name is `Chain` or `ShortCircuit`
2. Extracts the type argument from the generic call
3. Walks the step's super-type hierarchy to find `IStep<TIn, TOut>`
4. Places a `TIn -> TOut` hint after the closing parenthesis

PSI resolution is fully semantic — generics, inheritance chains, and type substitution are handled correctly without regex.

### Architecture

Dual-targeting project:
- **ReSharper** (.NET 4.7.2): Standalone extension
- **Rider** (Kotlin + .NET): .NET analysis runs in Rider's backend; Kotlin handles IDE integration

Both targets share the C# analysis code in `src/dotnet/ReSharperPlugin.Trax.Core/`.
