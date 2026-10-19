---
title: Runs
description: "Reference for the operations console's Runs area: the All, Failed, Queued and Dead letters views, their filters and actions, and their detail pages."
type: reference
area: dashboard
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ExecutionsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/WorkQueuePage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/WorkQueueDetailPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/DeadLettersPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/DeadLetterDetailPage.tsx
---

# Runs

One list of runs, with four views as tabs across the top. Each view is its own address, and its
filters are in the address too.

| View | Route | Lists |
|---|---|---|
| **All** | `/runs` | Every run, newest first |
| **Failed** | `/runs?state=FAILED` | All runs filtered to Failed, with where and why each failed |
| **Queued** | `/runs/queued` | The work queue: runs asked for and waiting for a worker, and entries already dispatched or cancelled |
| **Dead letters** | `/runs/dead-letters` | Manifests whose runs failed until their retries ran out |

A run's own page is [A run](/docs/dashboard/a-run).

## All and Failed

Read through [`operations.executions`](/docs/sdk-reference/graphql-api/queries/executions), 25 rows a
page, paged by cursor. The total under the list can be an estimate on an unfiltered list.

| Column | Value |
|---|---|
| **Train** | The train's short name, linking the run |
| **Run** | The run's id |
| **State** | Pending, In progress, Completed, Failed or Cancelled, and **cancelling** once a cancel was requested |
| **Started**, **Duration** | When it started, and how long it took once it ended |
| **Running junction** | The step an in-progress run is on, on a host that records junction progress |
| **Manifest** | The manifest that scheduled it |
| **Parent** | The run that started it |
| **Failure class** | Transient, Conflict, Permanent or Unclassified, for a failed run ([failure classification](/docs/effect/failure-classification)) |

The Failed view replaces **Running junction** with **Failed at** (the junction) and **Reason**.

| Filter | Address | Matches |
|---|---|---|
| Train name | `train` | Part of the train's name |
| **State** | `state` | One state |
| **Order** | `order` | Newest or oldest first |
| **Failure class** | `failure` | One class |
| **After**, **Before** | `after`, `before` | Started in the range, in the browser's time zone |
| **External id** | `externalId` | Exactly that external id |
| **Parent id** | `parentId` | The runs that run started |
| **Host name** | `host` | Exactly that host's runs |
| **Hide scheduler trains** | | Leaves out the scheduler's own trains; a filter that names a train shows it whatever this says |

Beside the list, **Live feed** shows the last 100 run state changes as they happen. On the first
page of an unfiltered list, new runs and state changes are merged into the rows as they arrive.

**Cancel selected** requests cancellation of the ticked Pending and In progress runs, after a
confirmation, through `cancelExecutions`: a queued run never starts, and a running one stops at its
next junction. The answer counts what was flagged and says why any was skipped. With the list
filtered to Pending or In progress, **Cancel all N matching** cancels every run the filter matches
as a [bulk operation](/docs/dashboard/bulk-operations).

## Queued

The work queue, read through [`operations.workQueue`](/docs/sdk-reference/graphql-api/queries/work-queue).

| Column | Value |
|---|---|
| **Train**, **Entry** | The train, and the entry's id, each linking the entry |
| **Status** | Queued, Dispatched or Cancelled |
| **Created**, **Priority**, **Attempts** | When it was queued, its dispatch priority, and how many times dispatch tried it |
| **Subject** | The entry's [subject key](/docs/mediator/queued-work#two-mutations-for-one-record), when the train sets one. Entries with one subject run one at a time. Clicking it filters the list to that subject. Computed by your train, so it may carry record identifiers. |
| **Confirmed** | **Yes**, or **Staged** for a queued entry whose train's queue check has not finished, which dispatch does not take yet |
| **Manifest**, **Run**, **Replays** | The manifest that queued it, the run it started once dispatched, and the run whose recorded decisions its run replays |

Filters: train name, **Subject** (exact), **Manifest id**, **Status**. **Queue train** opens the
[Queue dialog](/docs/dashboard/trains#run-and-queue). **Cancel** on a queued row, or **Cancel
selected**, cancels entries after a confirmation; their runs never start. An entry a user's
state-machine draft queued is read-only to operators, and the refusal says so.

### A queued entry

`/runs/queued/{id}`, read through `workQueue.detail`: its id, external id, train, status, priority,
dispatch attempts, when it was created, scheduled, dispatched and confirmed, its subject, input type,
manifest, run, the run it replays decisions of ("None: it asks afresh" when none), and the dead
letter it requeues. **Waiting on** says which entry holds its subject: "Entry N, which is running
for the same subject", or "Entry N, which is ahead of it for the same subject". The input is shown
with its [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive) members masked by the
API. **Cancel entry** is offered while it is queued.

## Dead letters

Read through [`operations.deadLetters`](/docs/sdk-reference/graphql-api/queries/dead-letters). How to
resolve one is in [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup).

The view shows dead letters **By train and reason** by default, or as a **List** (`view=list`). The
grouped view reads the newest 500 and groups those whose train and reason are the same; a group
names its train, how many it holds and how many still await, its manifests, and when the latest
came. A group of one shows its row; a larger group opens on a click. When more than 500 match, the
page says so and links the list, which pages through every one.

| Column | Value |
|---|---|
| **Train**, **Dead letter** | The manifest's train, and the dead letter's id, each linking the dead letter |
| **Manifest** | The manifest, by its external id |
| **Status** | Awaiting intervention, Retried or Acknowledged |
| **Reason**, **Retries**, **Dead-lettered** | Why, the retry count it reached, and when |

Filters: **Manifest id**, **Status**.

| Action | Does |
|---|---|
| **Requeue** (a row) | Queues a new run of its manifest and marks the dead letter Retried |
| **Acknowledge** (a row) | Asks for a resolution note and marks it Acknowledged, without a retry |
| **Requeue selected**, **Acknowledge selected** | The same for the ticked rows that await intervention |
| **Requeue all** | Requeues every dead letter awaiting intervention, in the background on the server, after the word `all` is typed; the page follows the job and reports its count |
| **Acknowledge all** | Asks for a note and acknowledges every one awaiting intervention |
| **Requeue all N matching**, **Acknowledge all N matching** | With the list filtered to Awaiting intervention, a [bulk operation](/docs/dashboard/bulk-operations) over what the filter matches |

Each requeue has a **, ask afresh** twin, offered when a listed train asks a decider: the new run
asks its deciders again instead of replaying the failed run's decisions.

### A dead letter

`/runs/dead-letters/{id}`.

| Section | Shows |
|---|---|
| **Dead letter details** | Status, when it was dead-lettered, the retry count it reached, when and how it was resolved, its manifest (with a link to that manifest's dead letters), the run its retry started, and the reason |
| **Manifest** | The manifest's name, whether it is enabled, its schedule, max retries, timeout, priority and last successful run |
| **Most recent failure** | The latest failed run: its id, failure junction, exception, failure class, reason, times, stack trace and input |
| **Failed run history** | The manifest's failed runs, a page at a time, each linking its run |

While it awaits intervention it offers **Requeue**, **Requeue, ask afresh** and **Acknowledge**.
