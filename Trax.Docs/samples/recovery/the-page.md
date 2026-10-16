---
title: How the Recovery page works
description: "What the Recovery sample's React page reads from the host over GraphQL and how it draws the Food order and Workflows views."
type: explanation
area: samples
parent: Recovery
nav_order: 2
---

# How the Recovery page works

Part of the [Recovery](/docs/samples/recovery) sample tour.

The page is React 19, Vite and `graphql-ws`, on the client [`trax client`](/docs/reference/cli#frontend-clients-trax-client)
generates from the host's manifest (`src/trax/`). The host runs only persisted documents: the machine operations and
`runProgress`, which the host generates and registers itself, and the page's own document for each train it calls
(`src/graphql/*.graphql`), which `trax client --documents` writes into `trax.documents.json` and the host registers
when it starts. The page signs in as the demo user (`recovery-user`, `src/connection.ts`), so it sees only its own
drafts and runs and sends nothing under `operations`; requeueing, resuming and asking afresh are an operator's, in the
dashboard. Over the socket the key travels in the `connection_init` payload as `apiKey`, because a browser cannot set
headers on a WebSocket upgrade. Both views keep their panels a fixed
size, so nothing moves as a run fills them.

The code panels import the sample's `.cs` files raw at build time. A panel shows the part that acts (a train's chain
from `Junctions()` to `Resolve()`, or a machine's states from its first `m.In(...)`), re-indents each chain so the
step names sit in one column, and fits its font to the longest line between 12 and 11 pixels; a line still too long
wraps under itself with a hanging indent, and the line numbers stay the file's.

**Food order.** Place order arms the scenario's trouble with the generated `orderTrouble`, then sends the order
machine's own operations through its generated server, `start`, `addItem` and `placeOrder`, as
`scripts/recordings/order.mjs` does: `placeOrder` twice, 120 ms apart, under one request id. The laptop and the
phone each follow the order's push, `orderChanged`, on a socket of their own, and neither polls the order. The run
`Ordered` queued is the one the order's own answer names, `invokedRun { runId }`; the page follows it with
`runProgress` and reads `ordersTabletCalls` while the run lasts, so a tablet call
that went unanswered shows as a failed try, a retry and the next try of `SendToRestaurant`. Everything on screen is
worked out from that list of timed events (`src/order/orderView.ts`): the phone's status and notifications, the
machine's line to light (the `.On(...)` of the event sent, the `m.In(...)` or outcome line of the state reached, the
`.Invokes(...)` line while the run is on its junctions, the `.After(...)` line when the timer clears the cart), the
train's junction to light, and the timeline's boxes. **Reset** discards an order still in its cart, since a user's
open carts count against their drafts.

**Workflows.** **Run** sends the generated `startRun`, which schedules a one-off manifest for the caller
(`ScheduleOptions.StartedBy`) and answers the id of its first run. For each attempt the page:

1. subscribes to `runProgress(runId)`, then reads `runProgress` once and merges both by `position`, keeping whichever
   copy of a step is further along;
2. when the attempt fails and the scheduler queues its retry, follows the run the stream names in `retriedAs`, as
   the next attempt;
3. reads the decision journal as soon as a question's step arrives, and once more when the attempt ends.

The refund and topic map trains carry `[TraxRunProgress(Answers = true)]`, so their steps show the starter each
answer and the junctions on a track.

Junction events say whether an answer was `replayed`, not why one was not, nor what was asked. The sample adds a
small `[TraxQuery]` train, `decisionJournal(runId)`, that reads `IDataContext.RecordedDecisions` and the run's
`ReplayDecisionsOf` and `ReplayAbandoned`, for the run's starter only (`IRunStarters`). Each decision carries the question as Trax put it to the decider (its
instructions and each option's description), the answer the run acted on, the model and the decider, so the model
panel shows a card per question with its words and its answer: the choice and how sure (`Damaged, 91% sure`), or the
probability of yes and the track it took (`93% yes: the Yes track`). Its badge tells **replayed** from **asked
afresh: state changed** (a `replay_refused` reason) from **asked afresh: on purpose** (no replay link). 

The timeline is a fixed grid with one row per attempt and one box per node of the train's declared chain, as
`src/cases.ts` writes it down (`src/workflowView.ts`): a junction, a question (with its answer), a routing step (named by the
junction on the track the run took), and the checkpoint. A `Parallel` step's branches are laid side by side in the
row. A box fills while its step runs. The checkpoint's box reads **saved** on the attempt that stored it and
**restored** on an attempt that started from it, whose earlier boxes are restored with it: a retry that starts from
the checkpoint records only the steps after it. Each row ends in a status pill, **Completed**, **Crashed** or
**Recovered**.

A `ROUTE` step carries `replayed: false` even when the decision it routes on was replayed; the timeline and the model
panel read the question's step.

**Change the data during the backoff** sends the generated `changeCaseData` for the case the run reads, which only
the caller who started the run may change. Asking afresh, requeueing and resuming from `NotifyCustomer` are an
operator's: the [dashboard](/docs/dashboard) does each, on the same run.
