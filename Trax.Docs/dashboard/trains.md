---
title: Trains
description: "Reference for the operations console's Trains list and a train's page: its chain, recent runs by step, manifests, the machines that start it, Run and Queue."
type: reference
area: dashboard
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/TrainsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/TrainDetailPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/RunTrainDialog.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/QueueTrainDialog.tsx
---

# Trains

## The list

`/trains`. Every `IServiceTrain<TIn, TOut>` the host registers, from
[`operations.trains`](/docs/sdk-reference/graphql-api/queries/operations#trains), sorted by any
column.

| Column | Value |
|---|---|
| **Train** | Its short name, linking its page; the full name on hover |
| **Kind** | Query or Mutation, when the train is exposed through GraphQL |
| **Lifetime** | Transient, Scoped or Singleton |
| **Roles** | The roles its `[TraxAuthorize]` requires |

The filter (`q` in the address) matches part of the full name. **Hide scheduler trains** leaves out
the scheduler's own trains. Each row has **Queue** and **Run**.

## A train

`/trains/{full name}`. Its full name under the title, **Queue** and **Run**, and a **Range** of the
last 24 hours or the last 7 days (`range` in the address) for the parts that read a range.

| Part | Shows |
|---|---|
| Summary | Total runs, success rate (amber under 90%), failed, running, average duration, last success |
| **Chain** | The train's declared chain drawn as a graph. Each step carries a red count of the failed runs in the range that stopped there; under it, how many failed in the range, and how many stopped outside a drawn step. "At least" when there were more than 500. |
| **Recent runs** | A grid of the newest 20 runs, oldest on the left: the top row is each run's state, and a row per step gives that step's state in that run. Every cell opens its run. **All runs of this train** opens [Runs](/docs/dashboard/runs) filtered to it. |
| **Manifests** | Up to 10 manifests that run this train: each one's schedule in words and its next run, or Disabled, or "No run due" |
| **Started by machines** | Each state machine state that starts this train on entering it, linking that machine's instances in the state |
| **Runs over time** | Completed, failed and cancelled runs over the range, with their totals and the slowest bucket's 95th percentile duration |

A train that has never run says so and offers **Run now** and **Queue a run**. A train this host
does not register, or whose chain it cannot read, says so in place of the chain.

## Run and queue

Both open a dialog with a **Train** picker, one field per public property of the input type (a
drop-down for an enum, a checkbox for a boolean, text otherwise), and an **Input JSON** box that
overrides the fields; blank is `Unit`.

| Button | Calls | Then |
|---|---|---|
| **Run train** | `runTrain`: runs now through the host's job submitter, with no work queue entry | Opens the new run |
| **Queue train** | `queueTrain`, with a **Priority** of 0 to 31 | Opens the new queue entry |

A refusal (an unknown train, an input that does not read as the input type, a refusal the train's
queue check makes) keeps the dialog open with the API's reason. For a train that sets a subject key,
the Queue dialog says the entry waits while another for the same subject is ahead of it or running.
The Run dialog warns that Run skips the queue; the API refuses to run such a train for a caller
outside a trusted scope, which includes the console, so queue it instead. Why is in
[Where Trax checks authorization](/docs/security/where-authorization-is-checked#the-operations-surface-and-the-dashboard).

The caller needs the operations gate, and the train's own `[TraxAuthorize]` requirements apply on
top of it: passing the gate makes someone an operator, not a holder of every train's role. The
same holds for Requeue and Resume on [a run's page](/docs/dashboard/a-run).
