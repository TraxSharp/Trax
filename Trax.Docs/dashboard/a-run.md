---
title: A run
description: "Reference for a run's page in the operations console: its header and actions, the run drawn on its chain, the step panel and the tabs."
type: reference
area: dashboard
nav_order: 4
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/ExecutionDetailPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/RunGraphPanel.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/JunctionTimeline.tsx
---

# A run

`/runs/{id}`. One run of a train: a header with what it is and what can be done to it, the run drawn
on its train's chain, and tabs below. Read through `operations.executionDetail`,
[`operations.runGraph`](/docs/sdk-reference/graphql-api/queries/executions#rungraph) and the run's
links, and read again whenever a run changes.

## Header

The train's short name, its state, **Replay abandoned** when it was queued to replay an earlier
run's decisions and asked afresh because that replay could not be honoured, and **Cancellation
requested** once a cancel was asked for.

| Field | Value |
|---|---|
| **Train** | The train's full name, linking [its page](/docs/dashboard/trains#a-train) |
| **Duration**, **Started**, **Ended** | "running" until it ends |
| **Started by** | Each of: the machine instance whose state invoked it, the manifest that scheduled it, the parent run, the queue entry it was dispatched from, the dead letter it retries. "Started directly" when none. |
| **Running now** | The junction an in-progress run is on |

A failed run shows its failure under the header: the reason, the junction it failed at, its failure
class and its stack trace.

| Action | Offered | Does |
|---|---|---|
| **Cancel** | Pending or In progress, not yet cancelling | Requests cancellation; a running run stops at its next junction. A run a user's state-machine draft started is read-only to operators. |
| **Requeue** | Completed, Failed or Cancelled | Queues a new run with this run's saved input and opens its queue entry. When this run recorded decisions, the new run replays them. |
| **Requeue, ask afresh** | As Requeue, for a train that asks a decider | The new run asks its deciders again |
| **Resume** | The run has a saved input and the run graph says it can resume | Queues a run that skips to the step after this run's latest [checkpoint](/docs/sdk-reference/train-methods/checkpoint) and opens its queue entry |

Each asks for confirmation. A refusal shows the API's reason: a requeue needs the input saved
(`SaveTrainParameters()`), not truncated or masked, and a train this host still registers.

## Run graph

The train's [declared chain](/docs/sdk-reference/train-methods/chain-graph) with this run on it,
drawn as a graph: each step a node coloured by where the run left it, a routing step's tracks and a
`Parallel` step's branches as branches.

| Colour | Means |
|---|---|
| Green | Completed |
| Blue | Running |
| Red | Failed |
| Amber | Cancelled, or interrupted (started and never recorded an end in a run that has ended) |
| Accent colour | Restored from a checkpoint (a step before the point a resumed run started at) |
| Grey | Not reached, skipped, or not recorded (an `Extract`, `Seed` or `Resolve`, or a checkpoint the run did not store) |

A failed run opens with its failed step selected. Selecting a node opens its panel, and the
selection is in the address (`?step=`), so a link can open a run at a step.

| In the panel | Shows |
|---|---|
| Kind and state | The step's kind (Junction, Short circuit, Parallel, Checkpoint and the rest) and its state |
| **Node**, **On track**, **Runs**, **Takes**, **Gives** | Its node id, the track it is on, the junction it runs, and its input and output types |
| **Attempt** | For a run of a manifest |
| Badges | **checkpoint stored** (its place, never its content), **replayed** (an answer came from an earlier run), **decided at run time** |
| **Error** | For a failed or cancelled step: its failure class and exception type, never its message |
| **Steps recorded** | Each recorded step the node covers, with its duration and attempt |
| **Decisions** | For a question or route: its key, the answer, the confidence, the decider, and whether it was replayed; "withheld" for a [`[TraxSensitive]`](/docs/sdk-reference/attributes/trax-sensitive#on-a-question-type) answer |
| **Resume from here** | On a failed or cancelled run, on each step the [resume check](/docs/sdk-reference/train-methods/checkpoint#what-can-resume-where) allows: queues a run that skips to this step, on what the checkpoint before it restores |

A step's own input and output are not recorded; the run's are under **Input and output**.

The graph is the chain as the host declares it now. When the chain changed after the run started,
a note says so: a step can then sit where the run did not run it. **Steps not on the graph** lists
recorded steps that match no node. A train with no graph on this host shows a message instead, and
its steps are in the timeline.

## Tabs

The open tab is in the address (`?tab=`).

| Tab | Shows |
|---|---|
| **Timeline** | One row per step the run took, a bar placed against the run's start and coloured by state; a running step extends to now. Selecting a row selects its node on the graph, and the graph's selection is lit here. Steps are recorded on a host that calls [`AddJunctionEvents()`](/docs/effect/junction-events); at most the first 500 are shown. |
| **Input and output** | The run's input and output as JSON, when the host saves train parameters. A train can withhold its output. |
| **Logs** | The run's log entries, oldest first, filtered by level, category and message, as on [Logs](/docs/dashboard/system#logs) |
| **Decisions** | Each question the run asked: the question, the answer (or "Withheld"), who decided it and when, and flags for **Replayed**, **Refused**, **Replay refused** and a withheld track or answer. **Details** shows the shadow answers, routes, fingerprint and state hash. |
| **Related** | The run's id, external id, scheduled time, executor, manifest, parent run, machine instance, queue entry and dead letter; the run whose decisions it replays and whether the replay held; the run it resumes and where; its child runs; and the host that ran it (name, environment, instance id, labels) |
