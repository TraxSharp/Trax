---
title: "6.4 Who may run PlaceOrder"
description: "Chapter 6 of the Trax book, section 4: Who may run PlaceOrder."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/customers-and-staff/roles-and-a-development-signing-key
---

# 6.4 Who may run PlaceOrder

`[TraxAuthorize]` on a train says who may run it.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Auth;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

[TraxAuthorize(Roles = Roles.Customer)]
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
            .Resolve();
}
```

The attribute is checked where a train is run on someone's behalf: through
`ITrainExecutionService`, which the GraphQL API of the next chapter also uses. It is not checked
when code that is already trusted runs a train directly, through its interface or the train bus,
as the tests do and as the scheduler will. [Where authorization is
checked](/docs/security/where-authorization-is-checked) has the full map.

## The customer is the caller

The customer used to be a field of the input, which let anyone order as anyone. It comes from the
caller now, and the input carries only what the caller chooses: the lines and the payment token.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderInput.cs
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required List<OrderLine> Lines { get; init; }
    public required string PaymentToken { get; init; }
}
```

One junction asks who the caller is and puts the answer in Memory, and every later junction reads
it from there. Only this junction depends on how a caller is known, so only it changes if that
does.

`Trains/PlaceOrder/Models/Customer.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/Customer.cs
namespace Shopfront.Trains.PlaceOrder.Models;

internal record Customer(string Id);
```

`Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class IdentifyCustomerJunction(TraxPrincipal principal)
    : EffectJunction<PlaceOrderInput, Customer>
{
    public override Task<Customer> Run(PlaceOrderInput input) =>
        Task.FromResult(new Customer(principal.Id));
}
```

`TraxPrincipal` is the signed-in caller: their qualified id, display name and roles. Resolving it
when nobody is signed in throws, so a run with no caller fails at this junction rather than
placing an order for nobody.

The junctions that used the input's customer take the `Customer` from Memory instead.

`Trains/PlaceOrder/Junctions/AssessRiskJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/AssessRiskJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class AssessRiskJunction(IShopfrontDbContext db)
    : EffectJunction<(Customer Customer, PricedCart Cart), OrderRisk>
{
    public override async Task<OrderRisk> Run((Customer Customer, PricedCart Cart) input)
    {
        var ordered = await db.Orders.AnyAsync(
            o => o.CustomerId == input.Customer.Id,
            CancellationToken
        );
        return new OrderRisk(input.Customer.Id, input.Cart.Total, FirstOrder: !ordered);
    }
}
```

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    ILogger<TakePaymentJunction> logger
)
    : EffectJunction<
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation),
        Payment
    >
{
    public override async Task<Payment> Run(
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation) input
    )
    {
        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Customer.Id,
                input.Order.PaymentToken,
                input.Cart.Total,
                CancellationToken
            );
        }
        catch (Exception ex)
        {
            // However the charge failed, the order stops here, so its stock goes back, even when
            // the run itself was cancelled.
            await catalogue.Release(
                [.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))],
                CancellationToken.None
            );
            logger.LogInformation("Released reservation {ReservationId}", input.Reservation.Id);

            if (ex is PaymentDeclinedException)
                throw new TrainException(ex.Message);
            throw;
        }

        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            charge.TransactionId
        );
        return new Payment(charge.TransactionId, input.Cart.Total, charge.ReceiptUrl);
    }
}
```

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(Customer Customer, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (Customer Customer, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Customer.Id,
            Total = input.Cart.Total,
            TransactionId = input.Payment.TransactionId,
            PlacedAt = DateTime.UtcNow,
            Items =
            [
                .. input.Cart.Lines.Select(l => new OrderItem
                {
                    Sku = l.Sku,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                }),
            ],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(CancellationToken);

        logger.LogInformation("Confirmed order {OrderId}", order.Id);
        return new PlaceOrderOutput(
            order.Id,
            order.Total,
            order.TransactionId,
            input.Payment.ReceiptUrl
        );
    }
}
```

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.5 A customer reads their own orders](/docs/book/customers-and-staff/a-customer-reads-their-own-orders)
