---
title: "11.8 Kill the worker mid-run"
description: "Chapter 11 of the Trax book, section 8: Kill the worker mid-run."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/a-separate-worker/run-both
---

# 11.8 Kill the worker mid-run

Place another order, and kill the worker while it packs, within the ten seconds. Kill it, not stop
it: Ctrl+C asks the worker to finish what it is running first, which is the point of stopping one.

```bash
pkill -9 -f Shopfront.Worker
```

The fulfilment's run says `In progress` in the console, and stays that way, because nothing is
running it. Start the worker again. It does not pick the run up: the job was claimed and started,
and a job that started is never started twice, since running it again might pack a parcel twice.

The hub sees to it instead. Every run in progress longer than its timeout is given up on, and in
Development that is about a minute. The failure hook tells staff:

```text
warn: Shopfront.Staff.LoggingStaffNotifier[0]
      For staff: Shopfront.Trains.FulfilOrder.IFulfilOrderTrain failed: Job was stuck InProgress beyond the configured stale in-progress timeout
```

Two settings decide when, and both are shortened in Development. `DefaultJobTimeout`, 20 minutes by
default, is how long a job may run before the hub cancels it. `StaleInProgressTimeout`, an hour by
default, is how long after that a run still in progress is failed. Shortening only the second
changes nothing, because the hub waits for the first.

The run is `Failed` now, and its reason says why. A fulfilment has no manifest, so nothing retries
it. A member of staff decides: open the run in the console and choose
**Re-queue**, which runs it again with the input it was given, on whichever worker claims it.

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.9 Test it](/docs/book/a-separate-worker/test-it)
