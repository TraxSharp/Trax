---
title: Bulk operations
description: "Reference for bulk operations in the operations console: one action over every row a list filter matches, the typed-count check, and its page."
type: reference
area: dashboard
nav_order: 9
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/ApplyToAllMatching.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/BulkOperationsPage.tsx
---

# Bulk operations

A bulk operation applies one action to every row a list's filter matches, however many that is,
where the batch bar's buttons act only on the ticked rows. It runs in batches on the server and is
read back by id. The GraphQL side, with every limit, is on
[operations: bulk operations](/docs/sdk-reference/graphql-api/mutations/bulk-operations).

## Starting one

Tick any row of a list, and the batch bar offers the list's actions for **all N matching**, N being
the list's count for its filter as set:

| List | Actions | Offered when |
|---|---|---|
| [Runs](/docs/dashboard/runs#all-and-failed) | **Cancel all N matching** | The list is filtered to Pending or In progress |
| [Dead letters](/docs/dashboard/runs#dead-letters) | **Requeue all N matching**, **Acknowledge all N matching** | The list is filtered to Awaiting intervention |
| [Manifests](/docs/dashboard/schedules#manifests) | **Queue all N matching**, **Enable all N matching**, **Disable all N matching** | Always |

The confirmation says what will happen to each row and asks for the matched count to be typed
before its button enables. Acknowledging asks for the resolution note first. The server matches the
filter again when it starts; when the count there is not the one confirmed, it starts nothing, and
the console shows the new count and asks again. A started operation opens on its own page. Only
one runs per host at a time: while one runs, starting another opens the running one.

## The list

`/system/operations`. The operation running on this host and those that finished in the last day,
at most 100: what it does, its status (Running, Finished, Failed or Canceled), a progress bar, how
many it matched, how many are done and failed, who started it, and when. A host keeps them in
memory: one started on another host is read there, and a restart forgets them.

## One operation

`/system/operations/{id}`, read every second while it runs.

| Part | Shows |
|---|---|
| Header | What it does (and "asking afresh"), who started it, when it started and stopped |
| **Counts** | Matched, done, failed, and still to do (or not attempted, once it stopped), with the server's message |
| **The filter it ran over** | Each filter field and value, and **Open the list with this filter** |
| **Why ids did not take** | Each refusal reason with how many ids it covers |
| **Each id that did not take** | Each failed id, linking its page, with its reason, 50 a page. Only the first 10,000 keep their id; every one is counted by reason. |
