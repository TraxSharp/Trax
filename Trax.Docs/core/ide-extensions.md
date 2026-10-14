---
title: IDE extensions
description: "The VSCode and Rider/ReSharper extensions that show each Chain and ShortCircuit call's junction input and output types as inlay hints, and how to install them."
type: reference
area: tooling
nav_order: 5
source:
  - Trax.Core/plugins/vscode/trax-hints
  - Trax.Core/plugins/rider-resharper
---

# IDE extensions

Inlay hint extensions for VSCode and for Rider and ReSharper. Each shows a junction's `TIn -> TOut`
after the chain call that names it.

```csharp fragment
protected override Task<Either<Exception, OrderReceipt>> Junctions() =>
    Chain<CheckInventoryJunction>()
        .Chain<ChargePaymentJunction>()      // InventoryResult -> PaymentConfirmation
        .Chain<CreateShipmentJunction>()     // PaymentConfirmation -> OrderReceipt
        .Resolve();
```

| | VSCode | Rider / ReSharper |
|---|---|---|
| Name | Trax.Core Chain Hints (`trax-hints`) | Trax.Core Chain Hints |
| Install | [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints) | JetBrains Marketplace, or build from source |
| Activates on | C# files | C# files |
| Calls hinted | `.Chain<T>()`, `.ShortCircuit<T>()` and `.IChain<T>()` written after a dot, with a non-generic type argument | Every `Chain`, `ShortCircuit` and `IChain` invocation with a type argument |
| How types are found | The editor's go-to-definition on the junction, reading the generic arguments of its class declaration | The semantic model, walking the junction's base types to `IJunction<TIn, TOut>` |
| Source | `Trax.Core/plugins/vscode/trax-hints` | `Trax.Core/plugins/rider-resharper` |

A chain's first step, written without a leading dot (`Chain<CheckInventoryJunction>()` above), gets
no hint in VSCode.

Types are resolved through the junction's base types, so these all show:

- direct inheritance: `class MyJunction : Junction<string, int>`
- primary constructors: `class MyJunction(ILogger logger) : EffectJunction<string, int>`
- generic type arguments: `EffectJunction<Unit, List<Manifest>>`
- tuples: `EffectJunction<(List<A>, List<B>), List<C>>`

## Building the Rider plugin from source

```bash
cd Trax.Core/plugins/rider-resharper
./gradlew buildPlugin
```

The plugin is written to `build/distributions/`.
