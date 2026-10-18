---
title: "10.6 Watch it fail"
description: "Chapter 10 of the Trax book, section 6: Watch it fail."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/failures-retries-and-dead-letters/schedule-it-with-retries
---

# 10.6 Watch it fail

Start the application and open the console as a member of staff, on **Executions**:

```bash
dotnet run --urls http://localhost:5080
```

The first restock runs at once, and the next a minute later. Take the supplier down:

```bash
curl -X POST http://localhost:5080/dev/supplier/down
```

The next restock fails, and the hook tells staff. Then it runs again and fails again, and again
after a longer wait:

```text
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (Shopfront.Suppliers.SupplierUnavailableException) with Message (The supplier's feed did not answer.).
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Shopfront.Trains.Restock.IRestockTrain failed: The supplier's feed did not answer.
```

The runs' start times show the backoff. Each retry runs on the scheduler's next pass after its
delay has gone by, so the gaps are a little longer than five and ten seconds:

```text
 id | train_state | failure_class |          start_time
----+-------------+---------------+-------------------------------
  6 | completed   | unclassified  | 2026-10-18 20:16:57.188407+00
 55 | failed      | transient     | 2026-10-18 20:18:02.674244+00
 65 | failed      | transient     | 2026-10-18 20:18:13.79591+00
 77 | failed      | transient     | 2026-10-18 20:18:27.971728+00
```

The console's **Executions** shows the three failed runs of `IRestockTrain`, each classified
`Transient`. **Dead letters** now holds the restock: the manifest stopped. It will not run again,
on its schedule or otherwise, until someone looks at it.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [10.7 Bring it back](/docs/book/failures-retries-and-dead-letters/bring-it-back)
