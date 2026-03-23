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
protected override OrderReceipt Junctions() =>
    Chain<CheckInventoryJunction>()
        .Chain<ChargePaymentJunction>()
        .Chain<CreateShipmentJunction>();
```

The extensions annotate each junction with its resolved types:

```
.Chain<CheckInventoryJunction>()      // OrderRequest -> InventoryResult
.Chain<ChargePaymentJunction>()       // InventoryResult -> PaymentConfirmation
.Chain<CreateShipmentJunction>()      // PaymentConfirmation -> OrderReceipt
```

### Supported Methods

- `.Chain<TJunction>()`
- `.ShortCircuit<TJunction>()`

### Type Resolution

Types are resolved by walking the junction's type hierarchy to find `IJunction<TIn, TOut>`. This handles:
- Direct inheritance: `class MyJunction : Junction<string, int>`
- Primary constructors: `class MyJunction(ILogger logger) : EffectJunction<string, int>`
- Nested generics: `EffectJunction<Unit, List<Manifest>>`
- Tuple types: `EffectJunction<(List<A>, List<B>), List<C>>`

## VSCode

**Source:** `plugins/vscode/trax-hints/`

### Installation

Install from the [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints)

### Internals

Activates on C# files. Uses regex to find chain calls, then calls VSCode's definition provider to jump to each junction's source file and extract the generic arguments from the class definition.

Resolved types are cached per junction class. The cache clears when any `.cs` file is modified.

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
3. Walks the junction's super-type hierarchy to find `IJunction<TIn, TOut>`
4. Places a `TIn -> TOut` hint after the closing parenthesis

PSI resolution is fully semantic — generics, inheritance chains, and type substitution are handled correctly without regex.

### Architecture

Dual-targeting project:
- **ReSharper** (.NET 4.7.2): Standalone extension
- **Rider** (Kotlin + .NET): .NET analysis runs in Rider's backend; Kotlin handles IDE integration

Both targets share the C# analysis code in `src/dotnet/ReSharperPlugin.Trax.Core/`.
