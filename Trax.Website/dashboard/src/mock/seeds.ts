import type { MockSchemaOverrides } from "./build-mock-schema";
import { operationFixtures } from "./fixtures";
import { runGraph } from "./scenarios";
import { hashVariables } from "./variables-hash";
import type { RunGraph, RunGraphNode } from "../types";

// A small, stable seed for the WorkQueue list. Auto-mock ids change every call, which breaks
// read-after-write (a cancelled row would lose its identity on refetch). Fixed ids survive
// refetches, so the CancelWorkQueueEntries overlay can flip the selected rows to CANCELLED.
// This is a stand-in for the Phase 1 fixture layer, kept deliberately tiny.
const workQueueItems = [
  {
    id: 501,
    externalId: "wq-00000000000000000000000000000501",
    trainName: "Trax.Demo.Trains.OrderTrain",
    status: "QUEUED",
    createdAt: "2026-07-07T11:59:00.000Z",
    dispatchedAt: null,
    scheduledAt: null,
    priority: 0,
    dispatchAttempts: 0,
    manifestId: 41,
    metadataId: null,
    deadLetterId: null,
    inputTypeName: "OrderInput",
  },
  {
    id: 502,
    externalId: "wq-00000000000000000000000000000502",
    trainName: "Trax.Demo.Trains.EmailTrain",
    status: "QUEUED",
    createdAt: "2026-07-07T11:58:00.000Z",
    dispatchedAt: null,
    scheduledAt: null,
    priority: 5,
    dispatchAttempts: 0,
    manifestId: 42,
    metadataId: null,
    deadLetterId: null,
    inputTypeName: "EmailInput",
  },
  {
    id: 503,
    externalId: "wq-00000000000000000000000000000503",
    trainName: "Trax.Demo.Trains.ReportTrain",
    status: "QUEUED",
    createdAt: "2026-07-07T11:57:00.000Z",
    dispatchedAt: null,
    scheduledAt: null,
    priority: 0,
    dispatchAttempts: 1,
    manifestId: 43,
    metadataId: null,
    deadLetterId: null,
    inputTypeName: "ReportInput",
  },
  {
    id: 504,
    externalId: "wq-00000000000000000000000000000504",
    trainName: "Trax.Demo.Trains.OrderTrain",
    status: "DISPATCHED",
    createdAt: "2026-07-07T11:56:00.000Z",
    dispatchedAt: "2026-07-07T11:56:30.000Z",
    scheduledAt: null,
    priority: 0,
    dispatchAttempts: 1,
    manifestId: 41,
    metadataId: 88_001,
    deadLetterId: null,
    inputTypeName: "OrderInput",
  },
];

/** Auto-mock overrides that pin the WorkQueue list to a stable set of rows. */
export const workQueueSeed: MockSchemaOverrides = {
  resolvers: () => ({
    WorkQueueQueries: {
      workQueues: () => ({
        items: workQueueItems,
        totalCount: workQueueItems.length,
        isEstimatedCount: false,
        skip: 0,
        take: 25,
        nextCursor: null,
      }),
    },
  }),
};

// The captured fixtures predate the run graph, and the devhost they come from has none, so
// `dev:mock` answers a run's graph from the scenario's checkpointed train. A captured run
// that completed ran every step; any other run (captured failed or cancelled, or auto-mocked)
// stopped after its checkpoint and can resume, so the resume controls show.
function capturedState(metadataId: number): string | undefined {
  const data = operationFixtures.ExecutionDetail?.[hashVariables({ id: metadataId })] as
    | { operations?: { executionDetail?: { trainState?: string } | null } }
    | undefined;
  return data?.operations?.executionDetail?.trainState;
}

const ranEveryStep = (node: RunGraphNode): RunGraphNode => ({
  ...node,
  state: node.state === "RESTORED" ? "COMPLETED" : node.state,
  tracks: node.tracks?.map((t) => ({ ...t, nodes: t.nodes.map(ranEveryStep) })),
});

function devRunGraph(metadataId: number): RunGraph {
  if (capturedState(metadataId) === "COMPLETED") {
    const resumed = runGraph(952);
    return { ...resumed, metadataId, nodes: resumed.nodes.map(ranEveryStep) };
  }
  return { ...runGraph(902), metadataId };
}

/** The `dev:mock` overrides for what the captured fixtures do not hold. */
export const devMockSeed: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      runGraph: (_root: unknown, args: Record<string, unknown>) => devRunGraph(args.metadataId as number),
    },
  }),
};
