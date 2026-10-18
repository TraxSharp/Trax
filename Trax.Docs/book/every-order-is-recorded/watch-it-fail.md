---
title: "3.4 Watch it fail"
description: "Chapter 3 of the Trax book, section 4: Watch it fail."
type: tutorial
area: general
parent: "3. Every order is recorded"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/every-order-is-recorded/record-runs-in-postgres
---

# 3.4 Watch it fail

Start the application again:

```bash
dotnet run --urls http://localhost:5080
```

Place an order:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```json
{"orderId":"03cfec30-1b48-4a08-805b-12fb5f530311","total":12.00,"transactionId":"tx_4ba708d1","receiptUrl":"https://pay.example/receipts/tx_4ba708d1?key=c265bec1af1b47f5abf2f4c90daeb9d6"}
```

The customer gets the receipt link. Now pay with the declined token:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_chargeDeclined","lines":[{"sku":"TEE","quantity":3}]}'
```

```json
"The card was declined."
```

That is a `422`, and the log shows the three T-shirts going back:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.TakePaymentJunction[0]
      Released reservation 568df86f-59b2-437a-ab72-797459564555
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (Trax.Core.Exceptions.TrainException) with Message (The card was declined.).
```

Place four more orders of one mug with `tok_visa`. The provider answers three of them, and the
fourth, its fifth charge since the application started, times out with a `500`:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.TakePaymentJunction[0]
      Released reservation 26db9cae-1c44-47f8-a9aa-0f0d701ab713
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (System.TimeoutException) with Message (The payment provider did not answer.).
```

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [3.5 Read the runs back](/docs/book/every-order-is-recorded/read-the-runs-back)
