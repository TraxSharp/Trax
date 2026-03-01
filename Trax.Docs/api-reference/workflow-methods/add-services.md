---
layout: default
title: AddServices
parent: Workflow Methods
grand_parent: API Reference
nav_order: 6
---

# AddServices

Stores DI services into Memory so that subsequent steps can access them. Services are stored by their **interface type** (the generic type parameter), not their concrete type.

Has overloads for 1 through 7 services.

## Signatures

```csharp
public Train<TInput, TReturn> AddServices<T1>(T1 service)
public Train<TInput, TReturn> AddServices<T1, T2>(T1 service1, T2 service2)
public Train<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3)
public Train<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4)
public Train<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5)
public Train<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6)
public Train<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(T1 s1, T2 s2, T3 s3, T4 s4, T5 s5, T6 s6, T7 s7)
```

## Type Parameters

Each `T1` through `T7` should be an **interface type**. The service is stored in Memory under this interface type, enabling steps to resolve it by interface.

## Parameters

Each `service` / `s1..s7` is a service instance that implements the corresponding type parameter interface.

All services are **required** (non-null). Passing `null` throws an `Exception`.

## Returns

`Train<TInput, TReturn>` — the workflow instance, for fluent chaining.

## Example

```csharp
public class ProcessOrderWorkflow(
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
4. If a service's concrete type is not a class, sets the workflow exception.
5. If a service doesn't implement any of the specified interfaces, sets the workflow exception.

### Moq Proxy Handling

`AddServices` has special handling for [Moq](https://github.com/moq/moq4) mock objects. If a service is detected as a Moq proxy (e.g., `Mock<IMyService>().Object`), it's stored under the **mocked interface type** rather than the proxy's concrete type. This enables seamless testing with mocked dependencies.

## Remarks

- Use interface types as the generic parameters — `AddServices<IMyService>(myService)`, not `AddServices<MyService>(myService)`.
- Steps resolve services from Memory by their interface type during construction. See [Steps]({{ site.baseurl }}{% link usage-guide/steps.md %}) for how constructor injection works.
- For more than 7 services, split across multiple `AddServices` calls.
