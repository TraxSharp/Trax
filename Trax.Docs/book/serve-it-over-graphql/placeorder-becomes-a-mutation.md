---
title: "7.2 PlaceOrder becomes a mutation"
description: "Chapter 7 of the Trax book, section 2: PlaceOrder becomes a mutation."
type: tutorial
area: general
parent: "7. Serve it over GraphQL"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/serve-it-over-graphql/packages
---

# 7.2 PlaceOrder becomes a mutation

`[TraxMutation]` puts a train in the schema as a mutation. `GraphQLOperation.Run` says the
mutation runs the train and answers with its output, as `POST /orders` did; the other operation,
queueing, comes with the worker later in the book. `[TraxRunProgress]` lets whoever started a run
follow it.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
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
            .Resolve();
}
```

The field's name comes from the interface: `IPlaceOrderTrain` without its `I` and `Train` is
`placeOrder`. Its argument is the train's input, `PlaceOrderInput`, which has no customer in it,
so the schema has none either: there is nothing a caller could send to order as someone else.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [7.3 The catalogue and the orders become queries](/docs/book/serve-it-over-graphql/the-catalogue-and-the-orders-become-queries)
