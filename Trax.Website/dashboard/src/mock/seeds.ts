import type { MockSchemaOverrides } from "./build-mock-schema";

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
