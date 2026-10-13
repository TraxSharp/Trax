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

The page is React 19, Vite, Apollo Client and `graphql-ws`. The host runs only persisted documents, so the page
sends every operation by an id and never its text: the topic map's by the ids the host generates for the machine
(`src/topicMap/topic-map.documents.json`), and every other one by the ids in `src/persisted-operations.json`, which
the host registers when it starts (`PageDocuments`). Over the socket the key travels in the `connection_init`
payload as `apiKey`, because a browser cannot set headers on a WebSocket upgrade. Both views keep their panels a fixed
size, so nothing moves as a run fills them.

The code panels import the sample's `.cs` files raw at build time. A panel shows the part that acts (a train's chain
from `Junctions()` to `Resolve()`, or a machine's states from its first `m.In(...)`), re-indents each chain so the
step names sit in one column, and fits its font to the longest line between 12 and 11 pixels; a line still too long
wraps under itself with a hanging indent, and the line numbers stay the file's.

**Food order.** Place order arms the scenario's trouble with `dispatch { orderTrouble }`, then sends the order
machine's own operations, `machines { order { start addItem placeOrder } }`, as `scripts/recordings/order.mjs`
does: `placeOrder` twice, 120 ms apart, under one request id. The laptop and the phone each subscribe to
`machineChanged` for the order on a socket of their own, and neither polls the order. The page finds the run
`Ordered` queued with `operations.executions(trainName:, startedAfter:)`, follows its steps with `onJunctionEvent`
and `operations.junctionRuns`, and reads `discover { orders { tabletCalls } }` while the run lasts, so a tablet call
that went unanswered shows as a failed try, a retry and the next try of `SendToRestaurant`. Everything on screen is
worked out from that list of timed events (`src/order/orderView.ts`): the phone's status and notifications, the
machine's line to light (the `.On(...)` of the event sent, the `m.In(...)` or outcome line of the state reached, the
`.Invokes(...)` line while the run is on its junctions, the `.After(...)` line when the timer clears the cart), the
train's junction to light, and the timeline's boxes. **Reset** discards an order still in its cart, since a user's
open carts count against their drafts.

**Workflows.** For each attempt the page:

1. polls `operations.executions(manifestId:)` until the attempt's execution exists;
2. subscribes to `onJunctionEvent(metadataId:)`;
3. then reads `operations.junctionRuns(metadataId:)` and merges both by `position`, keeping
   whichever copy of a step is further along, because the stored rows trail the stream and steps
   published before the subscription started reach it only through the table;
4. polls `operations.runGraph(metadataId:)` while the attempt runs, and reads it once more when it ends;
5. reads the decision journal as soon as a question's step arrives, and once more when the attempt ends.

Junction events say whether an answer was `replayed`, not why one was not, nor what was asked. The sample adds a
small `[TraxQuery]` train, `decisionJournal(metadataId)`, that reads `IDataContext.RecordedDecisions` and the run's
`ReplayDecisionsOf` and `ReplayAbandoned`. Each decision carries the question as Trax put it to the decider (its
instructions and each option's description), the answer the run acted on, the model and the decider, so the model
panel shows a card per question with its words and its answer: the choice and how sure (`Damaged, 91% sure`), or the
probability of yes and the track it took (`93% yes: the Yes track`). Its badge tells **replayed** from **asked
afresh: state changed** (a `replay_refused` reason) from **asked afresh: on purpose** (no replay link). While nothing
runs and the run graph's next node is a question the attempt has not answered, the panel shows that question waiting
for the model, with the words an earlier attempt was asked, and the code lights the line that asks it.

The timeline is a fixed grid with one row per attempt and one box per node of the train's declared chain, read from
the run graph (`src/workflowView.ts`): a junction, a question (with its answer), a routing step (named by the
junction on the track the run took), and the checkpoint. A `Parallel` step's branches are laid side by side in the
row. A box fills while its step runs. The checkpoint's box reads **saved** on the attempt that stored it and
**restored** on an attempt that started from it, whose earlier boxes are restored with it: a run that resumed records
only the steps after its resume point, and its graph marks the rest `RESTORED`. Each row ends in a status pill,
**Completed**, **Crashed** or **Recovered**. The page reads the graph as `allNodes`, one flat list at any depth, and
rebuilds the tree from each node's `parentId` and `track`; each read is numbered and an answer older than one already
drawn is dropped, so a slow answer never draws an older graph over the final one.

A `ROUTE` step carries `replayed: false` even when the decision it routes on was replayed; the timeline and the model
panel read the question's step.

**Resume from NotifyCustomer** sends `resumeExecution` with the node id `NotifyCustomer#0` for the latest failed
attempt, then reads the work queue entry it answers with until the entry names its run, and follows that run in a
`resume` row, as **Ask afresh** follows a requeue.
