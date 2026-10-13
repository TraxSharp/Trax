import { gql, type DocumentNode } from "@apollo/client";
import persisted from "./persisted-operations.json";

// The host requires persisted operations: it runs only documents it holds, by id. The page's own documents
// live in persisted-operations.json, which the host registers when it starts (each under the id beside it)
// and the page sends by that id (apollo.ts). To change one, edit its document there and bump the version at
// the end of its id: the host refuses to change the shape of the answer under an id a page already uses.

/** The page's persisted documents, by operation name. */
export const PERSISTED: ReadonlyMap<string, { id: string; document: string }> = new Map(
  persisted.operations.map((operation) => [operation.name, operation]),
);

const document = (name: string): DocumentNode => {
  const operation = PERSISTED.get(name);
  if (!operation) throw new Error(`persisted-operations.json has no operation named ${name}.`);
  return gql(operation.document);
};

// The sample's own mutation: schedules a one-off manifest (Trax.Api has no ScheduleOnce operation).
export const START_RUN = document("StartRun");

export const CHANGE_CASE_DATA = document("ChangeCaseData");

// Every attempt of the manifest: the first run, then each retry.
export const EXECUTIONS = document("Executions");

export const EXECUTION = document("Execution");

// Subscribe first, then read the stored steps and merge by position: rows trail the stream.
export const ON_JUNCTION_EVENT = document("OnJunctionEvent");

export const JUNCTION_RUNS = document("JunctionRuns");

// The sample's query over trax.decision: why an answer was asked afresh.
export const DECISION_JOURNAL = document("DecisionJournal");

export const TRIGGER_ASK_AFRESH = document("TriggerAskAfresh");

export const REQUEUE = document("Requeue");

// Resumes a failed or cancelled run at a step (a node id from the run graph), restoring the checkpoint
// before it: the steps before the point are skipped, not run. Answers with the work queue entry's id.
export const RESUME = document("Resume");

// requeueExecution and resumeExecution answer with the work queue entry's id; its run appears once dispatched.
export const WORK_QUEUE_ENTRY = document("WorkQueueEntry");

// The run drawn on its train's declared chain: every node, its state, each routing step's tracks
// and each Parallel step's branches. Read as allNodes, one flat list at any depth, which treeOf
// rebuilds from each node's parentId and track: a query that followed nested tracks could go only
// as deep as the server's field-cycle limit allows.
export const RUN_GRAPH = document("RunGraph");

// ── The food order ──────────────────────────────────────────────────────────
// Arms what goes wrong with the next order placed (Trains/OrderDemo/OrderTroubleTrain.cs).
export const ORDER_TROUBLE = document("OrderTrouble");

// The order machine's own operations, machines { order { start addItem placeOrder discard } }: the server mints the
// order's id on start, prices the cart, and applies every transition.
export const ORDER_START = document("OrderStart");
export const ORDER_ADD_ITEM = document("OrderAddItem");
export const ORDER_PLACE = document("OrderPlace");
export const ORDER_DISCARD = document("OrderDiscard");

// The run the order's Ordered state queued: the first DeliverOrderTrain run since the order was placed.
export const DELIVERY_RUNS = document("DeliveryRuns");

// The calls the restaurant's tablet received: the inside of SendToRestaurant, where the tablet is tried again.
export const TABLET_CALLS = document("TabletCalls");
