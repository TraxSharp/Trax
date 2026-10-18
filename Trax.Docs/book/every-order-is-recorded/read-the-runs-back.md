---
title: "3.5 Read the runs back"
description: "Chapter 3 of the Trax book, section 5: Read the runs back."
type: tutorial
area: general
parent: "3. Every order is recorded"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/every-order-is-recorded/watch-it-fail
---

# 3.5 Read the runs back

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c \
  "select id, train_state, failure_junction, failure_class, failure_reason
   from trax.metadata order by id"
```

```text
 id | train_state |  failure_junction   | failure_class |            failure_reason
----+-------------+---------------------+---------------+--------------------------------------
  1 | completed   |                     | unclassified  |
  2 | completed   |                     | unclassified  |
  3 | failed      | TakePaymentJunction | permanent     | The card was declined.
  4 | completed   |                     | unclassified  |
  5 | completed   |                     | unclassified  |
  6 | completed   |                     | unclassified  |
  7 | failed      | TakePaymentJunction | transient     | The payment provider did not answer.
(7 rows)
```

Row 1 is the order from before the provider could fail. Each failed run says where it stopped and
whether it is worth trying again. Look at a completed run's output:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c \
  "select output from trax.metadata where id = 6"
```

```text
                                                                 output
----------------------------------------------------------------------------------------------------------------------------------------
 {"total": 12.00, "orderId": "154e628d-b68b-43d7-935a-12a4f05835ca", "receiptUrl": {"_redacted": true}, "transactionId": "tx_89fc3d4d"}
(1 row)
```

The receipt link is masked. So is the junction logger's entry for `TakePaymentJunction`, here with
its input and output type names left out:

```text
dbug: Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider[0]
      {
        "id": 0,
        "trainName": "Shopfront.Trains.PlaceOrder.IPlaceOrderTrain",
        "name": "TakePaymentJunction",
        "externalId": "d2189d4c58aa4183a8d70566f3f8fdce",
        "trainExternalId": "d3f9d761dbe84887b9d7b859711f5a77",
        "trainMetadataId": 2,
        "startTimeUtc": "2026-10-18T19:07:03.1630118Z",
        "endTimeUtc": "2026-10-18T19:07:03.1644088Z",
        "state": "IsRight",
        "hasRan": true,
        "outputJson": "{\u0022transactionId\u0022:\u0022tx_c6c0dd1a\u0022,\u0022amount\u0022:12.00,\u0022receiptUrl\u0022:{\u0022_redacted\u0022:true}}"
      }
```

`trainMetadataId` is the run's row in `trax.metadata`, which ties each junction's entry to its
run.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [3.6 Test it](/docs/book/every-order-is-recorded/test-it)
