import type { MockSchemaOverrides } from "./build-mock-schema";
import type { ChainStepKind, RunGraph, RunGraphNode, RunNodeState } from "../types";

// Reusable auto-mock overrides for interaction stories: deterministic, filterable data plus
// empty / error states, so play functions can exercise every control without a backend.

type Args = Record<string, unknown>;

function pagedResult(items: unknown[]): Record<string, unknown> {
  return { items, totalCount: items.length, isEstimatedCount: false, skip: 0, take: 25, nextCursor: null };
}

/** An empty paged result for `TypeName.fieldName` (drives the "No rows" empty state). */
export function emptyPage(typeName: string, fieldName: string): MockSchemaOverrides {
  return { resolvers: () => ({ [typeName]: { [fieldName]: () => pagedResult([]) } }) };
}

/** Make `TypeName.fieldName` throw, so the page renders its error banner. */
export function errorOverride(typeName: string, fieldName: string): MockSchemaOverrides {
  return {
    resolvers: () => ({
      [typeName]: {
        [fieldName]: () => {
          throw new Error("Simulated backend error");
        },
      },
    }),
  };
}

// ── Work queue ───────────────────────────────────────────────────────────
// 3 QUEUED (cancellable), 1 DISPATCHED, 1 CANCELLED. The resolver honours the status /
// trainName filters so the filter controls actually narrow the list.
// 602 is staged (QUEUED, not yet confirmed); 601 and 604 share the subject "order-42", and 604
// (dispatched) holds it.
const WORK_QUEUE_ROWS = [
  { id: 601, externalId: "wq-601", trainName: "Trax.Demo.Trains.OrderTrain",   status: "QUEUED",     createdAt: "2026-07-07T11:59:00.000Z", dispatchedAt: null, priority: 0, dispatchAttempts: 0, manifestId: 41, subjectKey: "order-42", confirmedAt: "2026-07-07T11:59:01.000Z" },
  { id: 602, externalId: "wq-602", trainName: "Trax.Demo.Trains.EmailTrain",   status: "QUEUED",     createdAt: "2026-07-07T11:58:00.000Z", dispatchedAt: null, priority: 5, dispatchAttempts: 0, manifestId: 42, subjectKey: null, confirmedAt: null },
  { id: 603, externalId: "wq-603", trainName: "Trax.Demo.Trains.ReportTrain",  status: "QUEUED",     createdAt: "2026-07-07T11:57:00.000Z", dispatchedAt: null, priority: 0, dispatchAttempts: 1, manifestId: 43, subjectKey: null, confirmedAt: "2026-07-07T11:57:01.000Z", replayDecisionsOf: 899 },
  { id: 604, externalId: "wq-604", trainName: "Trax.Demo.Trains.OrderTrain",   status: "DISPATCHED", createdAt: "2026-07-07T11:56:00.000Z", dispatchedAt: "2026-07-07T11:56:30.000Z", priority: 0, dispatchAttempts: 1, manifestId: 41, subjectKey: "order-42", confirmedAt: "2026-07-07T11:56:01.000Z" },
  { id: 605, externalId: "wq-605", trainName: "Trax.Demo.Trains.ArchiveTrain", status: "CANCELLED",  createdAt: "2026-07-07T11:55:00.000Z", dispatchedAt: null, priority: 0, dispatchAttempts: 0, manifestId: 44, subjectKey: null, confirmedAt: null },
];

// workQueue.detail for the rows above: 601 waits on 604 (running for the same subject); 603 has a
// masked input and replays run 899's decisions.
function workQueueDetail(id: number) {
  const row = WORK_QUEUE_ROWS.find((r) => r.id === id);
  if (!row) return null;
  return {
    replayDecisionsOf: null,
    ...row,
    scheduledAt: null,
    metadataId: row.status === "DISPATCHED" ? 88_001 : null,
    deadLetterId: null,
    inputTypeName: "Trax.Demo.Trains.OrderInput",
    input: id === 603 ? '{"orderId":42,"cardNumber":"[REDACTED]"}' : null,
    subjectHeldBy: id === 601 ? 604 : null,
    subjectQueuedBehind: null,
  };
}

export const workQueueScenario: MockSchemaOverrides = {
  resolvers: () => ({
    WorkQueueQueries: {
      workQueues: (_root: unknown, args: Args) => {
        let rows = WORK_QUEUE_ROWS.map((r) => ({ replayDecisionsOf: null as number | null, ...r }));
        const status = args.status as string | undefined;
        const trainName = args.trainName as string | undefined;
        if (status) rows = rows.filter((r) => r.status === status);
        if (trainName) rows = rows.filter((r) => r.trainName.toLowerCase().includes(trainName.toLowerCase()));
        if (args.subjectKey != null) rows = rows.filter((r) => r.subjectKey === args.subjectKey);
        if (args.manifestId != null) rows = rows.filter((r) => r.manifestId === args.manifestId);
        return pagedResult(rows);
      },
      detail: (_root: unknown, args: Args) => workQueueDetail(args.id as number),
    },
  }),
};

// ── Dead letters ─────────────────────────────────────────────────────────
// 3 AWAITING_INTERVENTION (actionable), 1 RETRIED, 1 ACKNOWLEDGED. Filter-aware.
const DEAD_LETTER_ROWS = [
  { id: 701, manifestId: 41, manifestName: "OrderManifest",   status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:59:00.000Z", reason: "Timeout after 3 attempts", retryCountAtDeadLetter: 3, resolvedAt: null, resolutionNote: null },
  { id: 702, manifestId: 42, manifestName: "EmailManifest",   status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:58:00.000Z", reason: "Validation failed",        retryCountAtDeadLetter: 2, resolvedAt: null, resolutionNote: null },
  { id: 703, manifestId: 43, manifestName: "ReportManifest",  status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:57:00.000Z", reason: "Null reference",          retryCountAtDeadLetter: 3, resolvedAt: null, resolutionNote: null },
  { id: 704, manifestId: 41, manifestName: "OrderManifest",   status: "RETRIED",               deadLetteredAt: "2026-07-07T11:56:00.000Z", reason: "Transient network error", retryCountAtDeadLetter: 3, resolvedAt: "2026-07-07T11:56:30.000Z", resolutionNote: null },
  { id: 705, manifestId: 44, manifestName: "ArchiveManifest", status: "ACKNOWLEDGED",          deadLetteredAt: "2026-07-07T11:55:00.000Z", reason: "Known upstream outage",   retryCountAtDeadLetter: 1, resolvedAt: "2026-07-07T11:55:30.000Z", resolutionNote: "Tracked in TICKET-42" },
];

export const deadLetterScenario: MockSchemaOverrides = {
  resolvers: () => ({
    DeadLetterQueries: {
      deadLetters: (_root: unknown, args: Args) => {
        const status = args.status as string | undefined;
        let rows = status ? DEAD_LETTER_ROWS.filter((r) => r.status === status) : DEAD_LETTER_ROWS;
        if (args.manifestId != null) rows = rows.filter((r) => r.manifestId === args.manifestId);
        return pagedResult(rows);
      },
    },
  }),
};

// ── Manifests ────────────────────────────────────────────────────────────
// Mixed enabled state + schedule types; filter-aware on isEnabled / scheduleType / nameContains.
const MANIFEST_ROWS = [
  { id: 801, externalId: "man-801", name: "Trax.Demo.Manifests.OrderManifest",   isEnabled: true,  scheduleType: "CRON",     cronExpression: "0 */5 * * * *", intervalSeconds: null, maxRetries: 3, timeoutSeconds: 120, lastSuccessfulRun: "2026-07-07T11:00:00.000Z", manifestGroupId: 1, dependsOnManifestId: null, priority: 0, replayDecisionsOnRetry: true },
  { id: 802, externalId: "man-802", name: "Trax.Demo.Manifests.EmailManifest",   isEnabled: true,  scheduleType: "INTERVAL", cronExpression: null, intervalSeconds: 60, maxRetries: 5, timeoutSeconds: null, lastSuccessfulRun: null, manifestGroupId: 1, dependsOnManifestId: null, priority: 0, replayDecisionsOnRetry: true },
  { id: 803, externalId: "man-803", name: "Trax.Demo.Manifests.ReportManifest",  isEnabled: false, scheduleType: "NONE",     cronExpression: null, intervalSeconds: null, maxRetries: 1, timeoutSeconds: null, lastSuccessfulRun: null, manifestGroupId: 2, dependsOnManifestId: 801, priority: 0, replayDecisionsOnRetry: false },
  { id: 804, externalId: "man-804", name: "Trax.Demo.Manifests.ArchiveManifest", isEnabled: false, scheduleType: "CRON",     cronExpression: "0 0 * * * *", intervalSeconds: null, maxRetries: 2, timeoutSeconds: 300, lastSuccessfulRun: null, manifestGroupId: 2, dependsOnManifestId: null, priority: 0, replayDecisionsOnRetry: true },
];

// operations.manifestDetail for a row: its schedule details and its properties, with the
// [TraxSensitive] apiKey already masked as the API masks it.
function manifestDetail(id: number) {
  const row = MANIFEST_ROWS.find((r) => r.id === id);
  if (!row) return null;
  return {
    ...row,
    manifestGroupName: row.manifestGroupId === 1 ? "alpha-group" : "beta-group",
    propertyTypeName: "Trax.Demo.Trains.OrderInput",
    properties: id === 801 ? '{"region":"eu-west","apiKey":"[REDACTED]"}' : null,
    misfirePolicy: "FIRE_ONCE_NOW",
    misfireThresholdSeconds: id === 801 ? 60 : null,
    scheduledAt: null,
    nextScheduledRun: row.isEnabled ? "2026-07-07T12:05:00.000Z" : null,
    varianceSeconds: id === 801 ? 15 : null,
  };
}

// A manifest of one of the scheduler's own trains: listed only when hideAdminTrains is off.
const ADMIN_MANIFEST_ROW = { id: 805, externalId: "man-805", name: "Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain", isEnabled: true, scheduleType: "INTERVAL", cronExpression: null, intervalSeconds: 5, maxRetries: 0, timeoutSeconds: null, lastSuccessfulRun: null, manifestGroupId: 2, dependsOnManifestId: null, priority: 0, replayDecisionsOnRetry: true };

// A manifest deleted after the page listed it. The mock's batch trigger treats any manifest or
// group id at or above DELETED_ID_FLOOR (overlays.ts) as gone, so it is skipped with a note.
export const RETIRED_MANIFEST_ROW = { id: 1_000_001, externalId: "man-retired", name: "Trax.Demo.Manifests.RetiredManifest", isEnabled: false, scheduleType: "NONE", cronExpression: null, intervalSeconds: null, maxRetries: 1, timeoutSeconds: null, lastSuccessfulRun: null, manifestGroupId: 2, dependsOnManifestId: null, priority: 0, replayDecisionsOnRetry: true };

function manifestsResolver(extra: Record<string, unknown>[] = []) {
  return (_root: unknown, args: Args) => {
    let rows = [...MANIFEST_ROWS, ...extra] as (typeof MANIFEST_ROWS)[number][];
    if (args.hideAdminTrains === false) rows = [...rows, ADMIN_MANIFEST_ROW];
    if (args.isEnabled != null) rows = rows.filter((r) => r.isEnabled === args.isEnabled);
    if (args.scheduleType) rows = rows.filter((r) => r.scheduleType === args.scheduleType);
    const name = args.nameContains as string | undefined;
    if (name) rows = rows.filter((r) => r.name.toLowerCase().includes(name.toLowerCase()));
    return pagedResult(rows);
  };
}

export const manifestScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      manifests: manifestsResolver(),
      manifest: (_root: unknown, args: Args) => MANIFEST_ROWS.find((r) => r.id === args.id) ?? null,
      // The detail query ManifestDetailPage reads, keyed by id.
      manifestDetail: (_root: unknown, args: Args) => manifestDetail(args.id as number),
    },
  }),
};

/** The manifests list with one more row that was deleted after it was listed. */
export const manifestScenarioWithRetired: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: { manifests: manifestsResolver([RETIRED_MANIFEST_ROW]) },
  }),
};

// Manifest detail page: the manifest plus its stat cards, exclusion windows, and recent runs.
export const manifestDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      manifest: (_root: unknown, args: Args) =>
        MANIFEST_ROWS.find((r) => r.id === args.id) ?? MANIFEST_ROWS[0],
      manifestDetail: (_root: unknown, args: Args) => manifestDetail(args.id as number) ?? manifestDetail(801),
      manifestStats: (_root: unknown, args: Args) => ({
        manifestId: (args.manifestId as number) ?? 801,
        total: 42,
        completed: 30,
        failed: 8,
        inProgress: 2,
        pending: 1,
        cancelled: 1,
        lastRun: "2026-07-07T11:00:00.000Z",
        lastSuccessfulRun: "2026-07-07T10:55:00.000Z",
      }),
      manifestExclusions: () => [
        {
          type: "DAYS_OF_WEEK",
          daysOfWeek: ["SATURDAY", "SUNDAY"],
          dates: null,
          startDate: null,
          endDate: null,
          startTime: null,
          endTime: null,
        },
        {
          type: "TIME_WINDOW",
          daysOfWeek: null,
          dates: null,
          startDate: null,
          endDate: null,
          startTime: "02:00:00",
          endTime: "04:00:00",
        },
      ],
      executions: (_root: unknown, args: Args) =>
        args.manifestId == null
          ? pagedResult([])
          : pagedResult([
              exec(950, "Trax.Demo.Trains.OrderTrain", "COMPLETED", 1),
              exec(949, "Trax.Demo.Trains.OrderTrain", "FAILED", 2),
            ]),
    },
  }),
};

// ── Manifest groups ──────────────────────────────────────────────────────
const GROUP_ROWS = [
  { id: 1, name: "alpha-group", maxActiveJobs: 5, priority: 0, isEnabled: true, createdAt: "2026-07-01T00:00:00.000Z", updatedAt: "2026-07-07T11:00:00.000Z" },
  { id: 2, name: "beta-group", maxActiveJobs: null, priority: 3, isEnabled: false, createdAt: "2026-07-02T00:00:00.000Z", updatedAt: "2026-07-07T11:00:00.000Z" },
];

export const groupScenario: MockSchemaOverrides = {
  resolvers: () => ({
    ManifestGroupQueries: {
      groups: (_root: unknown, args: Args) => {
        const name = args.nameContains as string | undefined;
        const rows = name
          ? GROUP_ROWS.filter((r) => r.name.toLowerCase().includes(name.toLowerCase()))
          : GROUP_ROWS;
        return pagedResult(rows);
      },
      // Per-group stat columns, batched for the visible page (id 1 = alpha, id 2 = beta).
      stats: (_root: unknown, args: Args) =>
        ((args.groupIds as number[]) ?? []).map((id) => ({
          groupId: id,
          manifestCount: id === 1 ? 6 : 2,
          totalExecutions: id === 1 ? 100 : 40,
          completed: id === 1 ? 90 : 35,
          failed: id === 1 ? 7 : 3,
          inProgress: id === 1 ? 3 : 2,
          lastRun: "2026-07-07T11:00:00.000Z",
        })),
      // Global cross-group DAG: alpha (1) → beta (2).
      dependencyGraph: () => ({
        nodes: GROUP_ROWS.map((g) => ({ id: g.id, name: g.name, isHighlighted: false })),
        edges: [{ fromId: 1, toId: 2 }],
      }),
    },
  }),
};

/** The groups list with one more group that was deleted after it was listed. */
export const groupScenarioWithRetired: MockSchemaOverrides = {
  resolvers: () => ({
    ManifestGroupQueries: {
      groups: () =>
        pagedResult([
          ...GROUP_ROWS,
          { id: 1_000_002, name: "retired-group", maxActiveJobs: null, priority: 0, isEnabled: true, createdAt: "2026-07-03T00:00:00.000Z", updatedAt: "2026-07-07T11:00:00.000Z" },
        ]),
      stats: () => [],
      dependencyGraph: () => ({ nodes: [], edges: [] }),
    },
  }),
};

// ── Cluster (hosts) ────────────────────────────────────────────────────────
export const clusterScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      hosts: () => [
        {
          instanceId: "api-7f3c9a2b1e",
          name: "trax-api-1",
          environment: "Production",
          lastSeen: new Date().toISOString(),
          totalExecutions: 12045,
          currentlyRunning: 3,
        },
        {
          instanceId: "scheduler-2a9d4c8f0b",
          name: "trax-scheduler-1",
          environment: "Production",
          lastSeen: new Date(Date.now() - 8 * 60_000).toISOString(),
          totalExecutions: 98230,
          currentlyRunning: 0,
        },
      ],
    },
  }),
};

// ── Train detail ─────────────────────────────────────────────────────────
export const trainDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      trainStats: (_root: unknown, args: Args) => ({
        trainName: args.trainName as string,
        total: 420,
        completed: 400,
        failed: 15,
        inProgress: 2,
        pending: 3,
        cancelled: 0,
        lastRun: "2026-07-07T11:59:00.000Z",
        lastSuccessfulRun: "2026-07-07T11:58:00.000Z",
        averageMilliseconds: 1234.5,
      }),
      executions: () =>
        pagedResult([
          exec(950, "Trax.Demo.Trains.OrderTrain", "COMPLETED", 1),
          exec(949, "Trax.Demo.Trains.OrderTrain", "FAILED", 2),
        ]),
    },
  }),
};

// ── Executions ───────────────────────────────────────────────────────────
// Distinct names (so the live feed's simulator names stand out), mixed states, two pages.
// Filter-aware (trainState, trainName), order-aware (NEWEST/OLDEST), and paginated.
function exec(id: number, name: string, state: string, mins: number) {
  return {
    id,
    externalId: `exec-${id}`,
    name,
    trainState: state,
    startTime: new Date(1_783_166_400_000 - mins * 60_000).toISOString(),
    endTime: state === "IN_PROGRESS" || state === "PENDING" ? null : new Date(1_783_166_400_000 - mins * 60_000 + 12_000).toISOString(),
    failureJunction: state === "FAILED" ? "ValidateStep" : null,
    failureReason: state === "FAILED" ? "Assertion failed" : null,
    manifestId: null,
    hostName: "mock-host",
    failureClass: state === "FAILED" ? "TRANSIENT" : "UNCLASSIFIED",
    cancellationRequested: false,
    parentId: null as number | null,
    currentlyRunningJunction: state === "IN_PROGRESS" ? "ProcessStep" : null,
  };
}
// 902 was started from inside run 800; 901 runs on its own host, which the hostName filter finds.
const EXEC_PAGE_1 = [
  exec(903, "Trax.Exec.AlphaJob", "COMPLETED", 1),
  { ...exec(902, "Trax.Exec.BetaJob", "FAILED", 2), parentId: 800 },
  { ...exec(901, "Trax.Exec.GammaJob", "IN_PROGRESS", 3), hostName: "gamma-host" },
];
const EXEC_PAGE_2 = [
  exec(900, "Trax.Exec.DeltaJob", "COMPLETED", 4),
  exec(899, "Trax.Exec.EpsilonJob", "CANCELLED", 5),
];

export const executionScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      executions: (_root: unknown, args: Args) => {
        if (args.afterId === 900) {
          return { items: EXEC_PAGE_2, totalCount: 5, isEstimatedCount: false, skip: 0, take: 25, nextCursor: null };
        }
        let rows = [...EXEC_PAGE_1];
        const state = args.trainState as string | undefined;
        const name = args.trainName as string | undefined;
        const failureClass = args.failureClass as string | undefined;
        if (state) rows = rows.filter((r) => r.trainState === state);
        if (name) rows = rows.filter((r) => r.name.toLowerCase().includes(name.toLowerCase()));
        if (failureClass) rows = rows.filter((r) => r.failureClass === failureClass);
        // The exact-match filters, as the API serves them.
        if (args.externalId != null) rows = rows.filter((r) => r.externalId === args.externalId);
        if (args.parentId != null) rows = rows.filter((r) => r.parentId === args.parentId);
        if (args.hostName != null) rows = rows.filter((r) => r.hostName === args.hostName);
        if (args.order === "OLDEST") rows.reverse();
        const filtered = Boolean(
          state || name || failureClass || args.externalId != null || args.parentId != null || args.hostName != null,
        );
        return { items: rows, totalCount: 5, isEstimatedCount: false, skip: 0, take: 25, nextCursor: filtered ? null : 900 };
      },
    },
  }),
};

// ── Execution detail ─────────────────────────────────────────────────────
// Keyed by id: 903 is active (cancellable), 900 terminal (re-queueable), 800 has children, 902
// failed (a replaying retry of 899, a child of 800, with a junction timeline, and checkpoints it
// can resume from), 950 recorded more steps than the timeline shows, 952 resumed 902 after its
// checkpoint, 954 failed without a saved input.

// One node of a run graph; tracks and steps default to none.
function graphNode(id: string, kind: ChainStepKind, state: RunNodeState, fields: Partial<RunGraphNode> = {}): RunGraphNode {
  return {
    id,
    kind,
    opaque: false,
    replayed: false,
    checkpointed: false,
    canResume: false,
    state,
    steps: [],
    tracks: [],
    ...fields,
  };
}

const NO_GRAPH = { hasGraph: false, moreSteps: false, canResume: false, nodes: [], unmatchedSteps: [] };

/**
 * The run graph of a scenario run. 902 failed after its Findings checkpoint: the checkpoint is
 * stored, and it can resume at Summarize (the step after it, where it failed) or at Publish. 952 is
 * the run that resumed it there: the steps before are RESTORED. 954 (no saved input) has the same
 * checkpoint as 902, so its page draws "Resume from here" nowhere. Every other run's train has no
 * declared graph on this host.
 */
export function runGraph(metadataId: number): RunGraph {
  if (metadataId === 902 || metadataId === 954) {
    return {
      metadataId,
      hasGraph: true,
      moreSteps: false,
      canResume: true,
      nodes: [
        graphNode("Fetch#0", "CHAIN", "COMPLETED", { steps: [{ state: "COMPLETED", failureClass: null, failureException: null }] }),
        graphNode("Findings#1", "CHECKPOINT", "COMPLETED", { checkpointed: true }),
        graphNode("Summarize#2", "CHAIN", "FAILED", {
          canResume: true,
          steps: [{ state: "FAILED", failureClass: "TRANSIENT", failureException: "TimeoutException" }],
        }),
        graphNode("Route#3", "DECIDE", "NOT_REACHED", {
          tracks: [
            { name: "Short", description: "A summary under a page", isFallback: false, taken: false, nodes: [graphNode("Route#3/Short/Trim#0", "CHAIN", "NOT_REACHED")] },
            { name: "Long", description: null, isFallback: true, taken: false, nodes: [graphNode("Route#3/Long/Split#0", "CHAIN", "NOT_REACHED")] },
          ],
        }),
        graphNode("Publish#4", "CHAIN", "NOT_REACHED", { canResume: true }),
      ],
      unmatchedSteps: [],
    };
  }
  if (metadataId === 952) {
    return {
      metadataId,
      hasGraph: true,
      moreSteps: false,
      canResume: false,
      nodes: [
        graphNode("Fetch#0", "CHAIN", "RESTORED"),
        graphNode("Findings#1", "CHECKPOINT", "RESTORED", { checkpointed: true }),
        graphNode("Summarize#2", "CHAIN", "COMPLETED", { steps: [{ state: "COMPLETED", failureClass: null, failureException: null }] }),
        graphNode("Route#3", "DECIDE", "COMPLETED", {
          tracks: [
            { name: "Short", description: "A summary under a page", isFallback: false, taken: true, nodes: [graphNode("Route#3/Short/Trim#0", "CHAIN", "COMPLETED")] },
            { name: "Long", description: null, isFallback: true, taken: false, nodes: [graphNode("Route#3/Long/Split#0", "CHAIN", "SKIPPED")] },
          ],
        }),
        graphNode("Signals#4", "PARALLEL", "COMPLETED", {
          tracks: [
            { name: "Citations", description: null, isFallback: false, taken: true, nodes: [graphNode("Signals#4/Citations/Count#0", "CHAIN", "COMPLETED")] },
            { name: "Recency", description: null, isFallback: false, taken: true, nodes: [graphNode("Signals#4/Recency/Age#0", "CHAIN", "COMPLETED")] },
          ],
        }),
        graphNode("Publish#5", "CHAIN", "COMPLETED"),
      ],
      unmatchedSteps: [{ position: 9, name: "LegacyStep", nameWithheld: false, state: "COMPLETED" }],
    };
  }
  return { metadataId, ...NO_GRAPH };
}

function execDetail(id: number, name: string, state: string, childCount: number) {
  const done = state !== "IN_PROGRESS" && state !== "PENDING";
  const failed = state === "FAILED";
  return {
    id,
    externalId: `exec-${id}`,
    name,
    trainState: state,
    startTime: "2026-07-07T11:55:00.000Z",
    endTime: done ? "2026-07-07T11:55:12.000Z" : null,
    failureJunction: state === "FAILED" ? "ValidateStep" : null,
    failureReason: state === "FAILED" ? "Assertion failed" : null,
    failureException: null,
    stackTrace: null,
    input: '{"value":1}',
    output: null,
    manifestId: null,
    cancellationRequested: false,
    currentlyRunningJunction: state === "IN_PROGRESS" ? "ProcessStep" : null,
    junctionStartedAt: null,
    hostName: "mock-host",
    hostEnvironment: "Development",
    hostInstanceId: "i-mock",
    childCount,
    failureClass: failed ? "TRANSIENT" : "UNCLASSIFIED",
    parentId: id === 902 ? 800 : null,
    scheduledTime: id === 902 ? "2026-07-07T11:54:30.000Z" : null,
    executor: "JobRunner",
    hostLabels: id === 902 ? '{"region":"eu-west","pool":"blue"}' : null,
    replayDecisionsOf: id === 902 ? 899 : id === 951 ? 902 : null,
    replayAbandoned: id === 951,
    // 952 resumed 902 at Summarize, the step after its checkpoint.
    resumeFrom: id === 952 ? 902 : null,
    resumeAt: id === 952 ? "Summarize#2" : null,
  };
}

// The steps of a run, by metadata id (see execDetail). Positions start at 0, as the API's do.
function step(position: number, fields: Record<string, unknown>) {
  const startedAt = new Date(Date.parse("2026-07-07T11:55:00.000Z") + position * 2000).toISOString();
  return {
    position,
    kind: "JUNCTION",
    name: `Step${position}`,
    state: "COMPLETED",
    startedAt,
    endedAt: new Date(Date.parse(startedAt) + 1500).toISOString(),
    durationMs: 1500,
    failureClass: null,
    failureException: null,
    questionKey: null,
    answer: null,
    confidence: null,
    replayed: false,
    decider: null,
    answerWithheld: false,
    attempt: null,
    nameWithheld: false,
    trackPosition: null,
    ...fields,
  };
}

const JUNCTION_STEPS: Record<number, ReturnType<typeof step>[]> = {
  902: [
    step(0, { name: "ValidateOrder", attempt: 2 }),
    step(1, { kind: "YES_NO", name: "IsFraud", questionKey: "is-fraud", answer: "no", confidence: 0.92, replayed: true, endedAt: null, durationMs: null, attempt: 2 }),
    step(2, { name: "ChargeCard", state: "FAILED", failureClass: "TRANSIENT", failureException: "TimeoutException: the payment gateway did not answer", attempt: 2 }),
  ],
  903: [
    step(0, { name: "LoadCart" }),
    step(1, { kind: "ROUTE", name: "PickCarrier", questionKey: "carrier", answerWithheld: true, endedAt: null, durationMs: null }),
    step(2, { name: "Secret", nameWithheld: true, trackPosition: 1 }),
    step(3, { name: "ProcessStep", state: "IN_PROGRESS", endedAt: null, durationMs: null }),
  ],
  950: Array.from({ length: 501 }, (_, i) => step(i, {})),
};

function junctionRuns(args: Args) {
  const steps = JUNCTION_STEPS[args.metadataId as number] ?? [];
  const after = (args.afterPosition as number | null | undefined) ?? -1;
  const take = Math.min(Math.max((args.take as number | undefined) ?? 500, 1), 500);
  return steps.filter((s) => s.position > after).slice(0, take);
}

// The decisions a run recorded, by metadata id. 902: a yes/no a model answered, a score whose
// replay was refused, a choice about a sensitive type (answer withheld) whose route puts the next
// decision on a withheld track, and that decision. 903: one replayed answer the run refused. 950:
// 30 decisions, more than a page.
function decision(id: number, metadataId: number, fields: Record<string, unknown>) {
  return {
    id,
    metadataId,
    questionKey: null,
    occurrence: 0,
    kind: null,
    question: null,
    answer: null,
    refused: null,
    isRefused: false,
    fingerprint: null,
    model: null,
    decider: null,
    replayed: false,
    shadows: null,
    routes: null,
    stateHash: null,
    decidedAt: new Date(Date.parse("2026-07-07T11:55:00.000Z") + id * 1000).toISOString(),
    answerWithheld: false,
    trackWithheld: false,
    replayRefused: null,
    ...fields,
  };
}

const DECISIONS: Record<number, ReturnType<typeof decision>[]> = {
  902: [
    decision(1, 902, { questionKey: "is-fraud", kind: "yes_no", question: '{"question":"Is this order fraudulent?"}', answer: '"no"', fingerprint: "fp-fraud", model: "mock-model", decider: "Trax.Demo.Deciders.FraudDecider", stateHash: "k1:fraud", shadows: '[{"decider":"Trax.Demo.Deciders.ShadowDecider","answer":"no"}]' }),
    decision(2, 902, { questionKey: "risk", kind: "score", question: '{"question":"How risky is this order?"}', answer: '{"score":0.2,"replay_refused":"the question changed since the run it replays"}', replayRefused: "the question changed since the run it replays", fingerprint: "fp-risk", model: "mock-model", decider: "Trax.Demo.Deciders.RiskDecider" }),
    decision(3, 902, { questionKey: "Carrier", kind: "choice", question: '{"question":"Which carrier?"}', answerWithheld: true, fingerprint: "fp-carrier", model: "mock-model", decider: "Trax.Demo.Deciders.CarrierDecider", stateHash: "s1:carrier" }),
    decision(4, 902, { answerWithheld: true, trackWithheld: true }),
  ],
  903: [
    decision(5, 903, { questionKey: "is-fraud", kind: "yes_no", question: '{"question":"Is this order fraudulent?"}', answer: '"maybe"', refused: "the answer is not one of yes or no", isRefused: true, replayed: true, fingerprint: "fp-fraud" }),
  ],
  950: Array.from({ length: 30 }, (_, i) =>
    decision(100 + i, 950, { questionKey: `question-${i + 1}`, kind: "yes_no", answer: '"yes"', decider: "Trax.Demo.Deciders.BulkDecider" }),
  ),
};

function decisionPage(args: Args) {
  const all = DECISIONS[args.metadataId as number] ?? [];
  const after = (args.afterId as number | null | undefined) ?? null;
  const take = Math.min(Math.max((args.take as number | undefined) ?? 50, 1), 500);
  const items = all.filter((d) => after == null || d.id > after).slice(0, take);
  return { items, take, nextCursor: items.length ? items[items.length - 1].id : null };
}

// ── Logs ───────────────────────────────────────────────────────────────────
// A few entries of runs 902 and 903 across levels and categories. The resolver applies every filter
// operations.logs serves, in either order, keyset-paged. A text filter for "heartbeat" matches more
// than the cap, so it answers with a capped count, as the API does past 10,000 matches.
const LOG_ROWS = [
  { id: 1, metadataId: 902, eventId: 0, level: "INFORMATION", category: "Trax.Demo.Payments.Gateway", message: "Charging card for order 42", exception: null, stackTrace: null },
  { id: 2, metadataId: 902, eventId: 0, level: "DEBUG", category: "Trax.Demo.Context", message: "Payment context loaded", exception: null, stackTrace: null },
  { id: 3, metadataId: 902, eventId: 0, level: "WARNING", category: "Trax.Demo.Payments.Gateway", message: "Gateway slow, retrying", exception: null, stackTrace: null },
  { id: 4, metadataId: 902, eventId: 0, level: "ERROR", category: "Trax.Demo.Payments.Gateway", message: "Gateway timed out after 30s", exception: "TimeoutException", stackTrace: "at Trax.Demo.ChargeCard()" },
  { id: 5, metadataId: 903, eventId: 0, level: "INFORMATION", category: "Trax.Demo.Carts", message: "Loading cart", exception: null, stackTrace: null },
];
const LEVEL_ORDER = ["TRACE", "DEBUG", "INFORMATION", "WARNING", "ERROR", "CRITICAL", "NONE"];
const LOG_COUNT_CAP = 10_000;

function logsResolver(_root: unknown, args: Args) {
  const take = Math.min(Math.max((args.take as number | undefined) ?? 25, 1), 500);
  const oldest = args.order === "OLDEST";
  const message = (args.messageContains as string | null | undefined)?.toLowerCase();
  const category = (args.categoryContains as string | null | undefined)?.toLowerCase();
  if (message === "heartbeat" || category === "heartbeat") {
    const items = Array.from({ length: take }, (_, i) => ({
      id: 50_000 - i,
      metadataId: 0,
      eventId: 0,
      level: "TRACE",
      category: "Trax.Demo.Heartbeat",
      message: `heartbeat tick ${50_000 - i}`,
      exception: null,
      stackTrace: null,
    }));
    return { items, totalCount: LOG_COUNT_CAP, isEstimatedCount: false, isCountCapped: true, skip: 0, take, nextCursor: items[items.length - 1].id };
  }
  let rows = LOG_ROWS.filter((r) => args.metadataId == null || r.metadataId === args.metadataId);
  if (args.minimumLevel) rows = rows.filter((r) => LEVEL_ORDER.indexOf(r.level) >= LEVEL_ORDER.indexOf(args.minimumLevel as string));
  if (message) rows = rows.filter((r) => r.message.toLowerCase().includes(message));
  if (category) rows = rows.filter((r) => r.category.toLowerCase().includes(category));
  rows = oldest ? [...rows].sort((a, b) => a.id - b.id) : [...rows].sort((a, b) => b.id - a.id);
  const after = args.afterId as number | null | undefined;
  const total = rows.length;
  if (after != null) rows = rows.filter((r) => (oldest ? r.id > after : r.id < after));
  const items = rows.slice(0, take);
  const more = rows.length > take;
  return { items, totalCount: total, isEstimatedCount: false, isCountCapped: false, skip: 0, take, nextCursor: more ? items[items.length - 1].id : null };
}

export const logsScenario: MockSchemaOverrides = {
  resolvers: () => ({ LogQueries: { logs: logsResolver } }),
};

export const executionDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      executionDetail: (_root: unknown, args: Args) => {
        const id = args.id as number;
        if (id === 903) return execDetail(903, "Trax.Exec.AlphaJob", "IN_PROGRESS", 0);
        if (id === 800) return execDetail(800, "Trax.Exec.OmegaJob", "COMPLETED", 3);
        if (id === 902) return execDetail(902, "Trax.Exec.BetaJob", "FAILED", 0);
        if (id === 950) return execDetail(950, "Trax.Exec.LongJob", "COMPLETED", 0);
        if (id === 951) return execDetail(951, "Trax.Exec.RetryJob", "COMPLETED", 0);
        if (id === 952) return execDetail(952, "Trax.Exec.BetaJob", "COMPLETED", 0);
        if (id === 954) return { ...execDetail(954, "Trax.Exec.BetaJob", "FAILED", 0), input: null };
        return execDetail(900, "Trax.Exec.DeltaJob", "COMPLETED", 0);
      },
      runGraph: (_root: unknown, args: Args) => runGraph(args.metadataId as number),
      junctionRuns: (_root: unknown, args: Args) => junctionRuns(args),
      decisions: (_root: unknown, args: Args) => decisionPage(args),
      executionChildren: (_root: unknown, args: Args) =>
        pagedResult(
          args.parentId === 800
            ? [
                { id: 8001, name: "Trax.Exec.ChildOne", trainState: "COMPLETED", startTime: "2026-07-07T11:50:00.000Z", endTime: "2026-07-07T11:50:05.000Z" },
                { id: 8002, name: "Trax.Exec.ChildTwo", trainState: "FAILED", startTime: "2026-07-07T11:51:00.000Z", endTime: "2026-07-07T11:51:03.000Z" },
                { id: 8003, name: "Trax.Exec.ChildThree", trainState: "COMPLETED", startTime: "2026-07-07T11:52:00.000Z", endTime: "2026-07-07T11:52:04.000Z" },
              ]
            : [],
        ),
    },
    LogQueries: { logs: logsResolver },
  }),
};

// ── Scheduler config ───────────────────────────────────────────────────────
export const schedulerConfigScenario: MockSchemaOverrides = {
  resolvers: () => ({
    ConfigQueries: {
      scheduler: () => ({
        manifestManagerEnabled: true,
        jobDispatcherEnabled: true,
        manifestManagerPollingInterval: "PT5S",
        jobDispatcherPollingInterval: "PT2S",
        maxActiveJobs: 10,
        defaultMaxRetries: 3,
        defaultRetryDelay: "PT10S",
        retryBackoffMultiplier: 2,
        maxRetryDelay: "PT5M",
        defaultJobTimeout: "PT10M",
        stalePendingTimeout: "PT15M",
        recoverStuckJobsOnStartup: true,
        deadLetterRetentionPeriod: "P7D",
        autoPurgeDeadLetters: false,
        localWorkerCount: 4,
        metadataCleanupInterval: "PT1H",
        metadataCleanupRetention: "P30D",
        failureCountWindow: "P1D",
      }),
      environmentName: () => "Development",
      version: () => "1.46.0",
      // Trax.Scheduler was set at runtime. Microsoft.Hosting.Lifetime is one the mock host sets
      // itself after Trax, so a level saved for it is not the one in force (overlays.ts).
      logLevels: () => [
        { category: "Default", level: "Information", configuredLevel: "Information", overridden: false },
        { category: "Microsoft.AspNetCore", level: "Warning", configuredLevel: "Warning", overridden: false },
        { category: "Microsoft.Hosting.Lifetime", level: "Information", configuredLevel: "Information", overridden: false },
        { category: "Trax.Scheduler", level: "Debug", configuredLevel: "Information", overridden: true },
      ],
    },
  }),
};

// ── Trains registry ──────────────────────────────────────────────────────
const TRAIN_ROWS = [
  { fullName: "Trax.Trains.ZebraTrain", serviceTypeName: "Trax.Trains.ZebraTrain", implementationTypeName: "Trax.Trains.ZebraTrainImpl", inputTypeName: "Trax.Trains.ZebraInput", outputTypeName: "Unit", lifetime: "Scoped", isQuery: false, isMutation: true, graphQLName: "zebra", isBroadcastEnabled: false, hasQueueSubjectKey: true, requiredRoles: [], requiredPolicies: [], inputSchema: [{ name: "playerId", typeName: "String", isNullable: false, enumValues: null }, { name: "amount", typeName: "Int32", isNullable: true, enumValues: null }, { name: "tier", typeName: "PlayerTier", isNullable: false, enumValues: ["Bronze", "Silver", "Gold"] }, { name: "notify", typeName: "Boolean", isNullable: false, enumValues: null }] },
  { fullName: "Trax.Trains.AlphaTrain", serviceTypeName: "Trax.Trains.AlphaTrain", implementationTypeName: "Trax.Trains.AlphaTrainImpl", inputTypeName: "Unit", outputTypeName: "Unit", lifetime: "Singleton", isQuery: true, isMutation: false, graphQLName: "alpha", isBroadcastEnabled: true, hasQueueSubjectKey: false, requiredRoles: ["Admin"], requiredPolicies: [], inputSchema: [] },
  { fullName: "Trax.Trains.MangoTrain", serviceTypeName: "Trax.Trains.MangoTrain", implementationTypeName: "Trax.Trains.MangoTrainImpl", inputTypeName: "Unit", outputTypeName: "Unit", lifetime: "Transient", isQuery: false, isMutation: true, graphQLName: "mango", isBroadcastEnabled: false, hasQueueSubjectKey: false, requiredRoles: [], requiredPolicies: [], inputSchema: [] },
];

export const trainsScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: { trains: () => TRAIN_ROWS },
  }),
};

// ── Effects registry ───────────────────────────────────────────────────────
// One infrastructure (always-on) effect + one enabled (configurable) + one disabled toggleable effect.
// The configurable one has a setting of every kind: a switch, an enum, a number, a sensitive text
// setting that holds a value (never shown) and a predicate set in code.
export const JSON_EFFECT_FIELDS = [
  { name: "WriteOutputs", typeName: "Boolean", kind: "BOOLEAN", nullable: false, enumValues: null, sensitive: false, hasValue: true, value: "true", hint: "true or false" },
  { name: "LogLevel", typeName: "LogLevel", kind: "ENUM", nullable: false, enumValues: ["Debug", "Information", "Warning"], sensitive: false, hasValue: true, value: "Information", hint: "One of Debug, Information, Warning" },
  { name: "MaxBytes", typeName: "Int32", kind: "TEXT", nullable: true, enumValues: null, sensitive: false, hasValue: true, value: "1048576", hint: "Enter a whole number" },
  { name: "ConnectionString", typeName: "String", kind: "TEXT", nullable: true, enumValues: null, sensitive: true, hasValue: true, value: null, hint: "Enter text" },
  { name: "ShouldWrite", typeName: "Func`2", kind: "SET_IN_CODE", nullable: true, enumValues: null, sensitive: false, hasValue: true, value: null, hint: "set in code" },
];

export const effectsScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      effects: () => [
        {
          name: "PostgresContextProviderFactory",
          fullName: "Trax.Effect.Data.Postgres.PostgresContextProviderFactory",
          enabled: true,
          toggleable: false,
          isConfigurable: false,
          configurationTypeName: null,
          configuration: null,
          fields: [],
        },
        {
          name: "JsonEffectProviderFactory",
          fullName: "Trax.Effect.Provider.Json.JsonEffectProviderFactory",
          enabled: true,
          toggleable: true,
          isConfigurable: true,
          configurationTypeName: "Trax.Effect.Provider.Json.JsonEffectConfiguration",
          configuration: '{"logLevel":"Information","connectionString":"[REDACTED]"}',
          fields: JSON_EFFECT_FIELDS,
        },
        {
          name: "ParameterEffectProviderFactory",
          fullName: "Trax.Effect.Provider.Parameter.ParameterEffectProviderFactory",
          enabled: false,
          toggleable: true,
          isConfigurable: false,
          configurationTypeName: null,
          configuration: null,
          fields: [],
        },
      ],
    },
  }),
};

// ── Manifest group detail (group + dependency graph) ───────────────────────
export const groupDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    ManifestGroupQueries: {
      group: (_root: unknown, args: Args) =>
        GROUP_ROWS.find((r) => r.id === args.id) ?? GROUP_ROWS[0],
      graph: (_root: unknown, args: Args) => ({
        nodes: [
          { id: (args.groupId as number) ?? 1, name: "alpha-group", isHighlighted: true },
          { id: 2, name: "beta-group", isHighlighted: false },
        ],
        edges: [{ fromId: (args.groupId as number) ?? 1, toId: 2 }],
      }),
      stats: (_root: unknown, args: Args) =>
        ((args.groupIds as number[]) ?? []).map((id) => ({
          groupId: id,
          manifestCount: 3,
          totalExecutions: 120,
          completed: 90,
          failed: 20,
          inProgress: 10,
          lastRun: "2026-07-07T11:00:00.000Z",
        })),
    },
    OperationsQueries: {
      manifests: (_root: unknown, args: Args) =>
        args.manifestGroupId == null ? pagedResult([]) : pagedResult(MANIFEST_ROWS.slice(0, 2)),
      executions: (_root: unknown, args: Args) =>
        args.manifestGroupId == null
          ? pagedResult([])
          : pagedResult([
              exec(960, "Trax.Demo.Trains.OrderTrain", "COMPLETED", 1),
              exec(959, "Trax.Demo.Trains.EmailTrain", "FAILED", 2),
            ]),
    },
  }),
};

// ── Dead letter detail ─────────────────────────────────────────────────────
// Dead letter 701 (manifest 801, awaiting): the manifest panel, the latest failed run (962, with
// a stack trace and input) and the failed-runs list. 705 is acknowledged and has no failed runs.
export const deadLetterDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    DeadLetterQueries: {
      deadLetter: (_root: unknown, args: Args) => {
        const row = DEAD_LETTER_ROWS.find((r) => r.id === args.id);
        return row ? { ...row, manifestId: row.id === 701 ? 801 : 804, retryMetadataId: null } : null;
      },
    },
    OperationsQueries: {
      manifestDetail: (_root: unknown, args: Args) => manifestDetail(args.id as number),
      executions: (_root: unknown, args: Args) =>
        args.manifestId === 801 && args.trainState === "FAILED"
          ? pagedResult([
              exec(962, "Trax.Demo.Trains.OrderTrain", "FAILED", 3),
              exec(961, "Trax.Demo.Trains.OrderTrain", "FAILED", 9),
            ])
          : pagedResult([]),
      executionDetail: (_root: unknown, args: Args) => ({
        ...execDetail(args.id as number, "Trax.Demo.Trains.OrderTrain", "FAILED", 0),
        failureException: "TimeoutException",
        stackTrace: "at Trax.Demo.Trains.OrderTrain.ChargeCard()\nat Trax.Core.Train.RunAsync()",
        input: '{"orderId":42,"cardNumber":"[REDACTED]"}',
      }),
    },
  }),
};

// ── Persisted operations ───────────────────────────────────────────────────
const PERSISTED_OPERATION_ROWS = [
  { id: "greet.v1", tenantKey: null, operationName: "Greet", version: 1, document: "query Greet { operations { health { status } } }", shapeFingerprint: "aa11".repeat(16), isActive: true, deprecationReason: null, description: "Health probe", createdAt: "2026-07-01T00:00:00.000Z", updatedAt: "2026-07-07T10:00:00.000Z" },
  { id: "greet.v0", tenantKey: null, operationName: "Greet", version: 0, document: "query Greet { operations { health { description } } }", shapeFingerprint: "bb22".repeat(16), isActive: false, deprecationReason: "Superseded by greet.v1", description: null, createdAt: "2026-06-01T00:00:00.000Z", updatedAt: "2026-07-01T00:00:00.000Z" },
  { id: "orders.list", tenantKey: "acme", operationName: "Orders", version: 3, document: "query Orders { operations { trains { fullName } } }", shapeFingerprint: "cc33".repeat(16), isActive: true, deprecationReason: null, description: "Acme's order list", createdAt: "2026-07-02T00:00:00.000Z", updatedAt: "2026-07-06T00:00:00.000Z" },
];

export const persistedOperationsScenario: MockSchemaOverrides = {
  resolvers: () => ({
    PersistedOperationQueries: {
      persistedOperations: (_root: unknown, args: Args) => {
        const filter = (args.filter as Record<string, unknown> | null | undefined) ?? {};
        let rows = PERSISTED_OPERATION_ROWS;
        if (filter.isActive != null) rows = rows.filter((r) => r.isActive === filter.isActive);
        if (filter.tenantKey != null) rows = rows.filter((r) => (r.tenantKey ?? "") === filter.tenantKey);
        if (filter.idStartsWith) rows = rows.filter((r) => r.id.startsWith(String(filter.idStartsWith)));
        const skip = (args.skip as number) ?? 0;
        const take = (args.take as number) ?? 50;
        return { items: rows.slice(skip, skip + take), totalCount: rows.length };
      },
      persistedOperation: (_root: unknown, args: Args) =>
        PERSISTED_OPERATION_ROWS.find(
          (r) => r.id === args.id && (r.tenantKey ?? null) === ((args.tenantKey as string | null | undefined) || null),
        ) ?? null,
      persistedOperationHistory: (_root: unknown, args: Args) =>
        PERSISTED_OPERATION_ROWS.filter((r) => r.id === args.id).map((r, i) => ({
          historyId: 100 + i,
          id: r.id,
          tenantKey: r.tenantKey,
          document: r.document,
          shapeFingerprint: r.shapeFingerprint,
          changeType: "Upsert",
          changedAt: r.updatedAt,
          changedReason: null,
        })),
    },
  }),
};

/** A host that does not call UsePersistedOperations: the namespace probe fails. */
export const persistedOperationsUnavailable: MockSchemaOverrides = errorOverride(
  "OperationsQueries",
  "persistedOperations",
);

// ── State machines ───────────────────────────────────────────────────────
// Two machines, as the Recovery sample has them. source-partition is system-owned: ...0001 waits on
// a dispatched run (7101), ...0002 on a run still queued (work queue 7201), ...0003 waits on none.
// topic-map instances are users' drafts: ...0004 waits on its Building run, ...0005 on none. No row
// carries a context: operators never see one.
export const MACHINE_IDS = {
  dispatched: "3f2c1a00-0000-4000-8000-000000000001",
  queued: "3f2c1a00-0000-4000-8000-000000000002",
  idle: "3f2c1a00-0000-4000-8000-000000000003",
  draftBuilding: "3f2c1a00-0000-4000-8000-000000000004",
  draftIdle: "3f2c1a00-0000-4000-8000-000000000005",
};

function machineRow(
  machine: string,
  ownerKind: string,
  id: string,
  rowId: number,
  state: string,
  hasLiveInvokedRun: boolean,
  minute: number,
) {
  const updatedAt = `2026-07-07T11:${String(minute).padStart(2, "0")}:00.000Z`;
  return { machine, ownerKind, id, rowId, state, version: 1, createdAt: "2026-07-07T10:00:00.000Z", updatedAt, hasLiveInvokedRun };
}

export const MACHINE_ROWS = [
  machineRow("source-partition", "SYSTEM", MACHINE_IDS.dispatched, 11, "Ingesting", true, 58),
  machineRow("topic-map", "USER", MACHINE_IDS.draftBuilding, 21, "Building", true, 57),
  machineRow("source-partition", "SYSTEM", MACHINE_IDS.queued, 12, "Ingesting", true, 56),
  machineRow("topic-map", "USER", MACHINE_IDS.draftIdle, 22, "ChoosingRange", false, 55),
  machineRow("source-partition", "SYSTEM", MACHINE_IDS.idle, 13, "Ingested", false, 54),
];

function invokedRun(id: number, trainState: string, isLive: boolean, minute: number) {
  const done = trainState !== "IN_PROGRESS" && trainState !== "PENDING";
  return {
    id,
    externalId: `run-${id}`,
    name: id < 7100 || id === 7101 ? "Trax.Samples.Recovery.IIngestPartitionTrain" : "Trax.Samples.Recovery.IBuildTopicMapTrain",
    trainState,
    startTime: `2026-07-07T11:${String(minute).padStart(2, "0")}:00.000Z`,
    endTime: done ? `2026-07-07T11:${String(minute).padStart(2, "0")}:30.000Z` : null,
    failureClass: trainState === "FAILED" ? "TRANSIENT" : "UNCLASSIFIED",
    cancellationRequested: false,
    isLive,
  };
}

const INVOKED_RUNS: Record<string, ReturnType<typeof invokedRun>[]> = {
  [MACHINE_IDS.dispatched]: [invokedRun(7101, "IN_PROGRESS", true, 58), invokedRun(7090, "FAILED", false, 40)],
  [MACHINE_IDS.queued]: [],
  [MACHINE_IDS.idle]: [invokedRun(7050, "COMPLETED", false, 30)],
  [MACHINE_IDS.draftBuilding]: [invokedRun(7110, "IN_PROGRESS", true, 57)],
  [MACHINE_IDS.draftIdle]: [],
};

/** The instance a machineInstance lookup names, as the API looks it up; null when none matches. */
export function machineInstanceDetail(args: Args) {
  if (args.ownerKind === "USER" && args.rowId == null)
    throw new Error("A user's draft is named by its row id as well as its machine and id.");
  const row = MACHINE_ROWS.find(
    (r) =>
      r.machine === args.machine &&
      r.ownerKind === args.ownerKind &&
      r.id === args.id &&
      (args.rowId == null || r.rowId === args.rowId),
  );
  if (!row) return null;
  return {
    ...row,
    invokedRuns: INVOKED_RUNS[row.id] ?? [],
    isInvokedRunsCapped: row.id === MACHINE_IDS.idle,
    queuedInvokedRunEntryId: row.id === MACHINE_IDS.queued ? 7201 : null,
  };
}

function machineCounts(rows: typeof MACHINE_ROWS) {
  const counts = new Map<string, { machine: string; state: string; ownerKind: string; count: number }>();
  for (const r of rows) {
    const key = `${r.machine}|${r.state}|${r.ownerKind}`;
    const c = counts.get(key) ?? { machine: r.machine, state: r.state, ownerKind: r.ownerKind, count: 0 };
    c.count++;
    counts.set(key, c);
  }
  return [...counts.values()];
}

function machineInstancesPage(args: Args, capped = false) {
  let rows = MACHINE_ROWS;
  if (args.machine != null) rows = rows.filter((r) => r.machine === args.machine);
  if (args.state != null) rows = rows.filter((r) => r.state === args.state);
  if (args.ownerKind != null) rows = rows.filter((r) => r.ownerKind === args.ownerKind);
  const skip = Number(args.skip ?? 0);
  const take = Number(args.take ?? 25);
  return {
    items: rows.slice(skip, skip + take),
    totalCount: capped ? 10_000 : rows.length,
    isCountCapped: capped,
    isEstimatedCount: false,
    skip,
    take,
    nextCursor: null,
  };
}

/** The OperationsQueries resolvers of {@link machineScenario}. */
export const machineQueryResolvers = {
  machineInstanceCounts: (_root: unknown, args: Args) =>
    machineCounts(args.machine != null ? MACHINE_ROWS.filter((r) => r.machine === args.machine) : MACHINE_ROWS),
  machineInstances: (_root: unknown, args: Args) => machineInstancesPage(args),
  machineInstance: (_root: unknown, args: Args) => machineInstanceDetail(args),
};

export const machineScenario: MockSchemaOverrides = {
  resolvers: () => ({ OperationsQueries: machineQueryResolvers }),
};

/** More instances match than the API counts: the total stops at 10,000. */
export const machineScenarioCapped: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      machineInstanceCounts: () => machineCounts(MACHINE_ROWS),
      machineInstances: (_root: unknown, args: Args) => machineInstancesPage(args, true),
    },
  }),
};
