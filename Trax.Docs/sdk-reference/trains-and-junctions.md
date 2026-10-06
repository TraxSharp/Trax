---
layout: default
title: Trains and Junctions
description: Index of the train and junction base classes, the contracts they implement, the exception a failure carries and the helpers that register them.
parent: SDK Reference
nav_order: 0.5
has_children: true
---

# Trains and Junctions

The types you derive from and register: the train and junction base classes, the contracts they implement, the exception a failure carries, and the helpers that put them in the container. The methods you call inside `Junctions()` are under [Train Methods](/docs/sdk-reference/train-methods).

```csharp
public interface ICreateOrderTrain : IServiceTrain<CreateOrderInput, OrderResult>;

public class CreateOrderTrain : ServiceTrain<CreateOrderInput, OrderResult>, ICreateOrderTrain
{
    protected override Task<Either<Exception, OrderResult>> Junctions() =>
        Chain<ValidateOrder>().Chain<ChargePayment>().Chain<PersistOrder>().Resolve();
}

public class ChargePayment(IPaymentGateway gateway) : EffectJunction<ValidatedOrder, PaymentReceipt>
{
    public override Task<PaymentReceipt> Run(ValidatedOrder order) =>
        gateway.Charge(order.CustomerId, order.Total, CancellationToken);
}
```

| Page | Package | Description |
|------|---------|-------------|
| [Train](/docs/sdk-reference/trains-and-junctions/train) | Trax.Core | `Train<TInput, TReturn>`: the base class, with `Run`, `RunEither`, `ExternalId` and the chain methods |
| [ServiceTrain](/docs/sdk-reference/trains-and-junctions/service-train) | Trax.Effect | `ServiceTrain<TIn, TOut>` and `IServiceTrain<TIn, TOut>`: metadata, effects, lifecycle hooks, container-built junctions |
| [Junction](/docs/sdk-reference/trains-and-junctions/junction) | Trax.Core | `Junction<TIn, TOut>` and `IJunction<TIn, TOut>`: one unit of work on the railway |
| [EffectJunction](/docs/sdk-reference/trains-and-junctions/effect-junction) | Trax.Effect | `EffectJunction<TIn, TOut>`: a junction the junction effects can observe |
| [IRoute](/docs/sdk-reference/trains-and-junctions/i-route) | Trax.Core | `IRoute<TIn, TOut>`: the one-method contract every train implements |
| [MonadTask](/docs/sdk-reference/trains-and-junctions/monad-task) | Trax.Core | `MonadTask<TInput, TReturn>`: the awaitable link a chain step returns |
| [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | Trax.Core | `TrainException`, `TrainExceptionData` and `FailureClass`: how a failure is raised and recorded |
| [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration) | Trax.Effect | `AddScopedTraxRoute`, `AddTransientTraxRoute`, `AddSingletonTraxRoute` and the `*TraxJunction` aliases |
