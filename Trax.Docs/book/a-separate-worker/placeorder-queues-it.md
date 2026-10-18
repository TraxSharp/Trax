---
title: "11.4 PlaceOrder queues it"
description: "Chapter 11 of the Trax book, section 4: PlaceOrder queues it."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/a-separate-worker/fulfilment-is-a-train
---

# 11.4 PlaceOrder queues it

The last junction of `PlaceOrder` queues fulfilment for the order it just confirmed. It queues
through `ITrainExecutionService`, the same path a GraphQL caller's queued work takes, so the queued
train's own rules apply to it: its authorization, its queue hooks and its subject key.

`Shopfront.Core/Trains/PlaceOrder/Junctions/QueueFulfilmentJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/QueueFulfilmentJunction.cs
using System.Text.Json;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.TrainExecution;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class QueueFulfilmentJunction(
    ITrainExecutionService trains,
    ILogger<QueueFulfilmentJunction> logger
) : EffectJunction<(PlaceOrderOutput Order, PricedCart Cart), Unit>
{
    public override async Task<Unit> Run((PlaceOrderOutput Order, PricedCart Cart) input)
    {
        var queued = await trains.QueueAsync(
            typeof(IFulfilOrderTrain).FullName!,
            JsonSerializer.Serialize(
                new FulfilOrderInput
                {
                    OrderId = input.Order.OrderId,
                    Items = input.Cart.Lines.Sum(l => l.Quantity),
                }
            ),
            ct: CancellationToken
        );
        logger.LogInformation(
            "Queued fulfilment of {OrderId} as work queue entry {Entry}",
            input.Order.OrderId,
            queued.WorkQueueId
        );
        return Unit.Default;
    }
}
```

It comes last, after the order is stored, because a queued entry is written at once and is not
undone if the run fails later. It returns `Unit`, so the train's output is still the
`PlaceOrderOutput` the confirm made.

`Shopfront.Core/Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Auth;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

[TraxMutation(GraphQLOperation.Run)]
[TraxAuthorize(Roles = Roles.Customer)]
[TraxRunProgress]
public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<IdentifyCustomerJunction>()
            .Chain<PriceCartJunction>()
            .Chain<AssessRiskJunction>()
            .Gate<OrderRisk, LooksFraudulent>(gate =>
                gate.Yes(hold => hold.Chain<RefuseOrderJunction>())
                    .No(take => take)
                    .Shadow<ICandidateFraudDecider>()
                    .WaitForShadows(TimeSpan.FromSeconds(1))
            )
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Chain<QueueFulfilmentJunction>()
            .Resolve();
}
```

The warehouse joins the shop's services.

`Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Shopfront.Core/Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Warehouse;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself. Every process that runs the
    // trains calls it, because Trax checks every train's chain when a process starts.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString),
                ServiceLifetime.Scoped
            )
            .AddScoped<IShopfrontDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>().CreateDbContext()
            )
            .AddScoped<IShopfrontCaller, TraxShopfrontCaller>()
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>()
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>())
            .AddSingleton<IWarehouse, FakeWarehouse>();
}
```

A fulfilment is not a manifest, so the failure hook from chapter 10 would not hear about it. Its
rule becomes what it meant: staff hear about every failure but an order's, which is the customer's
to see.

`Shopfront.Core/Staff/StaffFailureHook.cs`:

```csharp compile=app file=Shopfront.Core/Staff/StaffFailureHook.cs
using Shopfront.Trains.PlaceOrder;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Shopfront.Staff;

// Tells staff about every failed run of the work nobody waits for. A failed order is the customer's.
public class StaffFailureHook(IStaffNotifier staff) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        metadata.Name == typeof(IPlaceOrderTrain).FullName
            ? Task.CompletedTask
            : staff.Notify($"{metadata.Name} failed: {exception.Message}", ct);
}
```

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.5 The hub](/docs/book/a-separate-worker/the-hub)
