---
title: "11.7 Run both"
description: "Chapter 11 of the Trax book, section 7: Run both."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/a-separate-worker/the-worker
---

# 11.7 Run both

In one terminal, the hub:

```bash
dotnet run --project Shopfront.Hub --urls http://localhost:5080
```

In a second, the worker:

```bash
dotnet run --project Shopfront.Worker --urls http://localhost:5081
```

Place an order as `ada`. `PlaceOrder` answers as soon as the order is confirmed, and the hub's log
shows fulfilment queued:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.QueueFulfilmentJunction[0]
      Queued fulfilment of 8fb0141d-1862-413b-9be1-b3940eef2453 as work queue entry 2
```

The worker's log picks it up and packs it, ten seconds later:

```text
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packing order 8fb0141d-1862-413b-9be1-b3940eef2453
info: Shopfront.Trains.FulfilOrder.Junctions.PackOrderJunction[0]
      Packed order 8fb0141d-1862-413b-9be1-b3940eef2453 as PKG-8FB0141D
```

In the console, **Cluster** now lists two processes, each named by its machine and process id, with
how many runs each has run. **Executions** records the machine each run ran on; on one machine that
is the same name for both, and on separate machines it tells them apart.

The supplier switch from chapter 10 is still on the hub, and the restock now runs on the worker,
whose fake supplier never hears about it. A fake's state lives in one process; the supplier a real
shop uses does not.

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.8 Kill the worker mid-run](/docs/book/a-separate-worker/kill-the-worker-mid-run)
