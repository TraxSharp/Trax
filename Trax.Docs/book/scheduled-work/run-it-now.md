---
title: "9.5 Run it now"
description: "Chapter 9 of the Trax book, section 5: Run it now."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/scheduled-work/schedule-them
---

# 9.5 Run it now

A new daily manifest first runs at its next 02:00, not when the host starts. Start the application,
and trigger the report as a member of staff instead of waiting:

```bash
dotnet run --urls http://localhost:5080
```

```bash
LIN=$(curl -s 'http://localhost:5080/dev/token/lin?staff=true')
curl -s http://localhost:5080/trax/graphql -H "Authorization: Bearer $LIN" \
  -H 'Content-Type: application/json' \
  -d '{"query":"mutation { operations { triggerManifest(externalId: \"stock-report\") { success message } } }"}'
```

```json
{"data":{"operations":{"triggerManifest":{"success":true,"message":"Manifest 'stock-report' triggered: queued a new run (work queue entry 1), due now."}}}}
```

In the console, the same is **Manifests**, then **stock-report**, then **Run now**. Within a few
seconds the report has run, and the check after it:

```text
info: Shopfront.Trains.StockReport.Junctions.CountStockJunction[0]
      Counted 13 units across 3 products
...
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Low on CAP, TEE.
```

Your counts are lower, after the orders of the earlier chapters.

Open the report's run in the console's **Executions**: its **Output** is the report, every product
with its stock. **Manifests** lists both, with the cron expression, and the check's parent.

**Executions** also has a run every few seconds that Shopfront never wrote: the scheduler's own
trains, the manifest manager that decides what is due and the dispatcher that hands it to a
worker. They are recorded like any train. The console hides them while **Hide admin trains** is
checked.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.6 Test it](/docs/book/scheduled-work/test-it)
