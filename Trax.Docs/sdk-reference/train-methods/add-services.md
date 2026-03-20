---
layout: default
title: AddServices
parent: Train Methods
grand_parent: SDK Reference
nav_order: 6
---

# AddServices

Stores DI services into Memory so that subsequent junctions can access them. Services are stored by their **interface type** (the generic type parameter), not their concrete type.

Has overloads for 1 through 7 services.

## Signatures

```csharp
public Monad<TInput, TReturn> AddServices<T1>(T1 service)
public Monad<TInput, TReturn> AddServices<T1, T2>(T1 service1, T2 service2)
public Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3)
public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4)
public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5)
public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6)
public Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6, T7 s7)
```

## Type Parameters

Each `T1` through `T7` should be an **interface type**. The service is stored in Memory under this interface type, enabling junctions to resolve it by interface.

## Parameters

Each `service` / `s1..s7` is a service instance that implements the corresponding type parameter interface.

All services are **required** (non-null). Passing `null` throws an `Exception`.

## Returns

`Monad<TInput, TReturn>` — the train instance, for fluent chaining.

## Example

```csharp
public class ProcessOrderTrain(
    IPaymentGateway paymentGateway,
    IInventoryService inventoryService,
    INotificationService notificationService
) : ServiceTrain<OrderInput, OrderResult>
{
    protected override async Task<Either<Exception, OrderResult>> RunInternal(OrderInput input)
    {
        return Activate(input)
            .AddServices<IPaymentGateway, IInventoryService, INotificationService>(
                paymentGateway, inventoryService, notificationService)
            .Chain<ValidateInventory>()    // Can access IInventoryService from Memory
            .Chain<ChargePayment>()        // Can access IPaymentGateway from Memory
            .Chain<SendReceipt>()          // Can access INotificationService from Memory
            .Resolve();
    }
}
```

## Behavior

1. For each service, finds the interface from the type parameter list that the service's concrete type implements.
2. Stores the service in Memory under that interface type.
3. If a service is `null`, throws an `Exception`.
4. If a service's concrete type is not a class, sets the train exception.
5. If a service doesn't implement any of the specified interfaces, sets the train exception.

### Moq Proxy Handling

`AddServices` has special handling for [Moq](https://github.com/moq/moq4) mock objects. If a service is detected as a Moq proxy (e.g., `Mock<IMyService>().Object`), it's stored under the **mocked interface type** rather than the proxy's concrete type. This enables seamless testing with mocked dependencies.

## Remarks

- Use interface types as the generic parameters — `AddServices<IMyService>(myService)`, not `AddServices<MyService>(myService)`.
- Junctions resolve services from Memory by their interface type during construction. See [Junctions](/docs/core/trains-and-junctions) for how constructor injection works.
- For more than 7 services, split across multiple `AddServices` calls.
