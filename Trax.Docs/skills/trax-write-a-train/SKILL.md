---
name: trax-write-a-train
description: Use when writing, adding, changing or splitting a Trax train or junction (ServiceTrain, Train, EffectJunction, Junction, Junctions(), Chain, Resolve, Memory), or when a train needs an error the caller can read, cancellation, a nested train, or a fix for a startup chain check refusal. Covers where each file goes, the train's own input type, its interface, internal junctions, how values pass between junctions, which exception reaches a caller, and registration with AddMediator.
metadata:
  internal: true
---

# Write a train

A train is a class whose `Junctions()` declares a chain of junctions. Each junction takes one
value from Memory and puts one back. The host reads every chain at startup and refuses to start
when one cannot run. [Trains and junctions](https://traxsharp.net/docs/core/trains-and-junctions)
explains the model; this skill is the order to do things in and the mistakes to avoid.

If the train has to choose between paths, load `trax-write-a-decider` as well. If the work spans
several requests or waits on a person, it may be a machine: load `trax-design-a-machine`.

## Decide first

- **`ServiceTrain` or `Train`.** Anything the host runs (through `ITrainBus`, GraphQL, the
  scheduler or a machine) is a `ServiceTrain<TIn, TOut>` from `Trax.Effect.Services.ServiceTrain`,
  with its own interface. A plain `Train<TIn, TOut>` from `Trax.Core.Train` has no database, no
  container and no hooks; use it only for code that runs with no host, constructed with `new`.
- **`EffectJunction` or `Junction`.** In a `ServiceTrain`, use `EffectJunction<TIn, TOut>`
  (`Trax.Effect.Services.EffectJunction`): it is recorded, and a cancel from another host can stop
  the run before it. A plain `Junction` works there but is invisible to the junction providers. An
  `EffectJunction` in a plain `Train` throws `TrainException`.
- **The input and output types.** One input type per train, never shared: `ITrainBus.RunAsync`
  finds the train by its input's type and reaches only the first train scanned for it.

## Steps

1. **Make the folder.** Read [Organize trains in a project](https://traxsharp.net/docs/core/organize-trains)
   before writing anything. Each train gets `Trains/<Name>/`:

   ```text
   Trains/PlaceOrder/
   ├── PlaceOrderRequest.cs       # input, public
   ├── OrderPlaced.cs             # output, public
   ├── IPlaceOrderTrain.cs        # interface, public
   ├── PlaceOrderTrain.cs         # train, public
   ├── Junctions/                 # one file per junction, internal
   └── Models/                    # internal unless the input or output is made of it
   ```

   One type per file. If the project came from a template, keep its sample trains (`HelloWorld`,
   `Lookup`) until yours are written and copy their folder layout; their junctions are `public`,
   but yours follow the page and are `internal`.

2. **Write the input, output and interface.** Then one junction per step, each `internal`, taking
   dependencies through its constructor. A type only the junctions hand each other goes in
   `Models/` as `internal`. Types that share a fence below are still one file each.

   ```csharp compile=train
   namespace Shop.Trains.PlaceOrder;

   public record PlaceOrderRequest(string Sku, int Quantity);

   public record OrderPlaced(long OrderId, decimal Total);
   ```

   ```csharp compile=train
   using Trax.Effect.Services.ServiceTrain;

   namespace Shop.Trains.PlaceOrder;

   public interface IPlaceOrderTrain : IServiceTrain<PlaceOrderRequest, OrderPlaced>;
   ```

   ```csharp compile=train
   namespace Shop.Trains.PlaceOrder.Models;

   internal record PricedOrder(string Sku, int Quantity, decimal Total);
   ```

   ```csharp compile=train
   using Shop.Trains.PlaceOrder.Models;
   using Trax.Core.Exceptions;
   using Trax.Effect.Services.EffectJunction;

   namespace Shop.Trains.PlaceOrder.Junctions;

   internal class PriceOrderJunction(ICatalog catalog)
       : EffectJunction<PlaceOrderRequest, PricedOrder>
   {
       public override async Task<PricedOrder> Run(PlaceOrderRequest input)
       {
           if (input.Quantity <= 0)
               throw new TrainException("Quantity must be at least 1.");

           var price = await catalog.PriceOf(input.Sku, CancellationToken);
           return new PricedOrder(input.Sku, input.Quantity, price * input.Quantity);
       }
   }

   internal class SaveOrderJunction(IOrderStore orders) : EffectJunction<PricedOrder, OrderPlaced>
   {
       public override async Task<OrderPlaced> Run(PricedOrder input)
       {
           var id = await orders.Save(input.Sku, input.Quantity, input.Total, CancellationToken);
           return new OrderPlaced(id, input.Total);
       }
   }
   ```

3. **Declare the chain.** `Junctions()` names junctions and ends in `Resolve()`, nothing else:

   ```csharp compile=train
   using Shop.Trains.PlaceOrder.Junctions;
   using Trax.Core.Functional;
   using Trax.Effect.Services.ServiceTrain;

   namespace Shop.Trains.PlaceOrder;

   public class PlaceOrderTrain : ServiceTrain<PlaceOrderRequest, OrderPlaced>, IPlaceOrderTrain
   {
       protected override Task<Either<Exception, OrderPlaced>> Junctions() =>
           Chain<PriceOrderJunction>().Chain<SaveOrderJunction>().Resolve();
   }
   ```

   Each junction's output lands in Memory under its declared type, and the next junction whose
   input is that type receives it. A value not in Memory is taken from the container. The
   train's output must be in Memory when `Resolve()` runs. `Extract`, `AddServices` and
   `ShortCircuit` cover the rest: [How a chain runs](https://traxsharp.net/docs/core/building-chains).

4. **Register it.** `AddMediator` scans assemblies and registers every `ServiceTrain` under its
   interface. It comes after `AddEffects` and is called once:

   ```csharp compile=train
   using Shop.Trains.PlaceOrder;
   using Trax.Effect.Data.InMemory.Extensions;
   using Trax.Effect.Extensions;
   using Trax.Mediator.Extensions;

   services.AddTrax(trax =>
       trax.AddEffects(effects => effects.UseInMemory())
           .AddMediator(typeof(PlaceOrderTrain).Assembly)
   );
   ```

   Callers inject `IPlaceOrderTrain` or call `ITrainBus.RunAsync<OrderPlaced>(request, ct)`.
   Exposing it over GraphQL (`[TraxQuery]`, `[TraxMutation]`, `[TraxAuthorize]`) is
   `trax-expose-over-api`.

5. **Start the host and read what it says.** The [startup chain check](https://traxsharp.net/docs/core/startup-chain-check)
   names the train, the step and the missing type for every fault at once. Fix what it names;
   do not reach for `SkipChainVerification()`.

## Errors

A junction fails by throwing; the rest of the chain is skipped and the caller gets that exception.
In the same process the caller sees it unchanged. A GraphQL caller does not: only an exception
whose type is exactly `TrainException` (`Trax.Core.Exceptions`) passes its message through, as
`TRAX_TRAIN_ERROR`. A subclass of it reads "The train failed.", and anything else, including
`InvalidOperationException`, is masked as "Unexpected Execution Error".

```csharp fragment
throw new InvalidOperationException("Out of stock");   // wrong: the API caller sees a masked error
throw new TrainException("Out of stock");              // right: the caller reads "Out of stock"
```

The message goes to the client as written, so it carries no secret and no other user's data.
`TrainException` takes a message only; to keep the cause, log it before you throw. Do not catch
an exception to return a default value, and do not turn error masking off. Details:
[Handle errors in trains](https://traxsharp.net/docs/cross-cutting/handle-errors-in-trains) and
[Error codes](https://traxsharp.net/docs/sdk-reference/graphql-api/error-codes).

## Cancellation

The run's token is the junction's `CancellationToken` property, set before `Run` is called. Pass
it to every async call, call `CancellationToken.ThrowIfCancellationRequested()` in long loops,
and never catch the `OperationCanceledException`. Code that starts a run passes its own token
(`trainBus.RunAsync<T>(input, ct)`). See
[Make a junction cancellable](https://traxsharp.net/docs/cross-cutting/make-a-junction-cancellable).

```csharp fragment
public override Task<Quote> Run(QuoteRequest input, CancellationToken ct)  // wrong: no such overload
public override Task<Quote> Run(QuoteRequest input)                        // right: use CancellationToken
```

## Known mistakes

| Wrong | Right |
|---|---|
| Input, interface, train and junctions in one file, or several trains in one folder | One type per file, one folder per train, as in step 1 |
| `public class ...Junction` | `internal class ...Junction`; the rest of the app talks to the interface |
| A shared base junction or shared input record written before a second train needs it | Share late, and only a generic junction, in `Trains/Shared/Junctions/`. One that grows conditionals per caller goes back to one per train |
| Two trains taking the same input type | A type per train |
| `if (TrainInput.X) ...` in `Junctions()` | `TrainInput` throws while the chain is declared. Read the input in a junction, or route with a decider (`trax-write-a-decider`) |
| Two junctions that both return `string` or `int` | Distinct types (`FirstName`, `OrderId`): Memory holds one value per type and the second overwrites the first |
| A junction returning the object it changed, to keep it in Memory | Memory holds references: change it in place and return `Unit` (from `Trax.Core.Functional`) |
| `[Inject]` properties on a junction | Constructor parameters. A junction's properties are never filled |
| Overriding `Run` on a `ServiceTrain`, or ending the chain with `Resolve(value)` | Both are refused. Work before or after the chain is a junction at its head or tail: [Move work into the chain](https://traxsharp.net/docs/core/move-work-into-the-chain) |
| Running a child train by passing the parent's `Metadata` to `RunAsync` | A junction that takes `ITrainBus` and calls `RunAsync` with the child's input and its `CancellationToken` |
| Installing `Trax.Core.Analyzers` for chain errors | It is deprecated and reports nothing. The startup chain check replaces it ([analyzer](https://traxsharp.net/docs/core/analyzer)) |

## Testing

`trax-test-your-app` covers testing trains. A chain can be checked without a host:
`ChainVerification.Verify(train.DeclaredChain(), typeof(TIn), typeof(TOut))` (`Trax.Core.Monad`)
returns the faults the startup check would report.

## Pages

[ServiceTrain](https://traxsharp.net/docs/sdk-reference/trains-and-junctions/service-train),
[EffectJunction](https://traxsharp.net/docs/sdk-reference/trains-and-junctions/effect-junction),
[TrainException](https://traxsharp.net/docs/sdk-reference/trains-and-junctions/train-exception),
[AddMediator](https://traxsharp.net/docs/sdk-reference/configuration/add-mediator) and the
[Train methods](https://traxsharp.net/docs/sdk-reference/train-methods). For the model behind
them: [Trains and junctions](https://traxsharp.net/docs/core/trains-and-junctions) and
[Memory](https://traxsharp.net/docs/core/memory).
