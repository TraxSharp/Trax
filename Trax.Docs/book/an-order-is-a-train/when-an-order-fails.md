---
title: "1.8 When an order fails"
description: "Chapter 1 of the Trax book, section 8: When an order fails."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/an-order-is-a-train/run-it
---

# 1.8 When an order fails

Add a cap to a mug:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","lines":[{"sku":"MUG","quantity":1},{"sku":"CAP","quantity":1}]}'
```

```json
"CAP is out of stock."
```

That is a `422`. The log shows the cart was priced, then the train stopped:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.PriceCartJunction[0]
      Priced 2 lines at 30.50
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (Trax.Core.Exceptions.TrainException) with Message (CAP is out of stock.).
```

`ReserveStockJunction` threw, so no payment was taken and no order was confirmed. `Run` rethrew the
exception as it was thrown, which is how `Program.cs` could turn it into a `422`. Because the
catalogue reserves all or nothing, the mug was not reserved either.

Send an empty cart and the order stops one junction earlier, at pricing:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","lines":[]}'
```

```json
"The cart is empty."
```

Stop the application with Ctrl+C.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.9 When the chain does not fit](/docs/book/an-order-is-a-train/when-the-chain-does-not-fit)
