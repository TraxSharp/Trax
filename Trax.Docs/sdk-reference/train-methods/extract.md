---
title: Extract
description: "Reference for Extract, which copies a property or field of type TOut from a TIn value in Memory, or a value handed to it, into its own Memory slot."
type: reference
area: trains
package: Trax.Core
namespace:
  - Trax.Core.Train
  - Trax.Core.Monad
source:
  - Trax.Core/src/Trax.Core/Train/Train.cs
  - Trax.Core/src/Trax.Core/Monad/Monad.Extract.cs
parent: Train Methods
grand_parent: SDK Reference
nav_order: 4
---

# Extract

Copies a member of type `TOut` out of a `TIn` value into Memory under `typeof(TOut)`, so a later junction can take it. The `TIn` is taken from Memory, or handed to the call.

## Signatures

```csharp fragment
// Train<TInput, TReturn>
protected Monad<TInput, TReturn> Extract<TIn, TOut>();
protected Monad<TInput, TReturn> Extract<TIn, TOut>(TIn input);

// MonadTask<TInput, TReturn>, later in a chain
public MonadTask<TInput, TReturn> Extract<TIn, TOut>();
public MonadTask<TInput, TReturn> Extract<TIn, TOut>(TIn input);
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `TIn` | The type to extract from. It has a public instance property or field of type `TOut`. |
| `TOut` | The type to extract and store in Memory |
| `input` | The value to extract from. Without it, `TIn` is taken from Memory. |

## Returns

The chain: `Monad<TInput, TReturn>` from the train's method, `MonadTask<TInput, TReturn>` from a later link.

## Behavior

1. Takes `TIn` from Memory, or uses `input`.
2. Looks for the first public instance property of type `TOut` on it, then the first public instance field. Static members and indexers are skipped.
3. Stores the value in Memory under `typeof(TOut)`.

Only the first matching member is read. Handing a value to the call (`Extract<TIn, TOut>(value)`) puts its type into Memory without a junction producing it; the startup check records it as a seed.

| Condition | Exception set on the train |
|-----------|---------------------------|
| No matching member, or its value is `null` | `Could not find non-null value of type: (<TOut>) in properties or fields for (<TIn>). Is it public?` |
| `TIn` is not in Memory (parameterless overload), a value type included | `Extract<OrderInput, OrderDetails> (train 'ProcessOrderTrain') found no 'OrderInput' in Memory. Chain a junction that outputs 'OrderInput' before the Extract.` |

## Example

```csharp compile
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

// OrderInput(string OrderId, OrderDetails Details)
public class ProcessOrderTrain : ServiceTrain<OrderInput, OrderResult>
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Extract<OrderInput, OrderDetails>()   // OrderDetails out of OrderInput
            .Chain<ProcessOrder>()            // takes OrderDetails from Memory
            .Resolve();
}
```

## See also

- [AddServices](/docs/sdk-reference/train-methods/add-services), the other way to seed Memory in the chain
- [Memory](/docs/core/memory)
