import type { MockSchemaOverrides } from "./build-mock-schema";
import type { ChainStepKind, RunGraph, RunGraphNode, RunGraphStep, RunNodeState } from "../types";
import type { StatefulOverlay } from "./store/overlays";
import { GraphQLError } from "graphql";
import { flattenRunGraph } from "../lib/runGraphTree";

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
  { id: 604, externalId: "wq-604", trainName: "Trax.Demo.Trains.OrderTrain",   status: "DISPATCHED", createdAt: "2026-07-07T11:56:00.000Z", dispatchedAt: "2026-07-07T11:56:30.000Z", priority: 0, dispatchAttempts: 1, manifestId: 41, metadataId: 88_001, subjectKey: "order-42", confirmedAt: "2026-07-07T11:56:01.000Z" },
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
  { id: 701, manifestId: 41, manifestExternalId: "order-nightly", manifestName: "OrderManifest",   status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:59:00.000Z", reason: "Timeout after 3 attempts", retryCountAtDeadLetter: 3, resolvedAt: null, resolutionNote: null },
  { id: 702, manifestId: 42, manifestExternalId: "email-digest", manifestName: "EmailManifest",   status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:58:00.000Z", reason: "Validation failed",        retryCountAtDeadLetter: 2, resolvedAt: null, resolutionNote: null },
  { id: 703, manifestId: 43, manifestExternalId: "report-weekly", manifestName: "ReportManifest",  status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:57:00.000Z", reason: "Null reference",          retryCountAtDeadLetter: 3, resolvedAt: null, resolutionNote: null },
  { id: 704, manifestId: 41, manifestExternalId: "order-nightly", manifestName: "OrderManifest",   status: "RETRIED",               deadLetteredAt: "2026-07-07T11:56:00.000Z", reason: "Transient network error", retryCountAtDeadLetter: 3, resolvedAt: "2026-07-07T11:56:30.000Z", resolutionNote: null },
  { id: 705, manifestId: 44, manifestExternalId: "archive-hourly", manifestName: "ArchiveManifest", status: "ACKNOWLEDGED",          deadLetteredAt: "2026-07-07T11:55:00.000Z", reason: "Known upstream outage",   retryCountAtDeadLetter: 1, resolvedAt: "2026-07-07T11:55:30.000Z", resolutionNote: "Tracked in TICKET-42" },
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

// Two manifests of one train dead-lettered for the same reason, and a third dead letter of that
// train that failed another way: the Dead letters view shows two groups, the first of two.
export const SHARED_DEAD_LETTER_REASON = "Max retries exceeded: (1) failures > (0) max retries";
const SHARED_REASON_ROWS = [
  { id: 721, manifestId: 45, manifestExternalId: "import-eu", manifestName: "Trax.Demo.Trains.ImportFeedTrain", status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:59:00.000Z", reason: SHARED_DEAD_LETTER_REASON, retryCountAtDeadLetter: 1, resolvedAt: null, resolutionNote: null },
  { id: 720, manifestId: 46, manifestExternalId: "import-us", manifestName: "Trax.Demo.Trains.ImportFeedTrain", status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:58:00.000Z", reason: SHARED_DEAD_LETTER_REASON, retryCountAtDeadLetter: 1, resolvedAt: null, resolutionNote: null },
  { id: 719, manifestId: 45, manifestExternalId: "import-eu", manifestName: "Trax.Demo.Trains.ImportFeedTrain", status: "AWAITING_INTERVENTION", deadLetteredAt: "2026-07-07T11:57:00.000Z", reason: "Timeout after 3 attempts", retryCountAtDeadLetter: 3, resolvedAt: null, resolutionNote: null },
];

export const deadLetterGroupScenario: MockSchemaOverrides = {
  resolvers: () => ({
    DeadLetterQueries: {
      deadLetters: (_root: unknown, args: Args) => {
        const status = args.status as string | undefined;
        return pagedResult(status ? SHARED_REASON_ROWS.filter((r) => r.status === status) : SHARED_REASON_ROWS);
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

// scheduleText and nextRuns(count:) for a manifest row, as the API reads them: the schedule in
// words, and the next times it runs (none for a disabled manifest or one with no time).
const MANIFEST_SCHEDULE_TEXT: Record<number, string> = {
  801: "Every 5 minutes",
  802: "Every minute",
  803: "Only when triggered",
  804: "Every hour at :00",
  805: "Every 5 seconds",
};

const MANIFEST_RUN_STEP_SECONDS: Record<number, number> = { 801: 300, 802: 60, 804: 3600, 805: 5 };

type ScheduledRow = { id: number; isEnabled: boolean };

export const manifestScheduleResolvers = {
  scheduleText: (row: ScheduledRow) => MANIFEST_SCHEDULE_TEXT[row.id] ?? "Only when triggered",
  nextRuns: (row: ScheduledRow, args: Args) => {
    const step = MANIFEST_RUN_STEP_SECONDS[row.id];
    if (!row.isEnabled || step == null) return [];
    const count = Math.min(Math.max((args.count as number | undefined) ?? 5, 0), 10);
    const first = Date.parse("2026-07-07T12:05:00.000Z");
    return Array.from({ length: count }, (_, i) => new Date(first + i * step * 1000).toISOString());
  },
};

// operations.metrics.series: every bucket of the range, oldest first, with a steady trickle of
// runs and a failure every seventh bucket. Deterministic, so stories and snapshots are stable.
const SERIES_BUCKETS: Record<string, { seconds: number; count: number }> = {
  LAST_HOUR: { seconds: 60, count: 60 },
  LAST24_HOURS: { seconds: 900, count: 96 },
  LAST7_DAYS: { seconds: 3600, count: 168 },
};

export function metricSeries(args: Args) {
  const range = (args.range as string | undefined) ?? "LAST24_HOURS";
  // The API refuses a 30-day series: the indexes cannot serve it inside the metrics budget.
  if (range === "LAST30_DAYS")
    throw new GraphQLError(
      "A 30-day series is not served: at a million runs it reads past the metrics budget on the existing indexes. Read the last 7 days instead.",
      { extensions: { code: "TRAX_METRICS_SERIES_ARGUMENT" } },
    );
  const { seconds, count } = SERIES_BUCKETS[range] ?? SERIES_BUCKETS.LAST24_HOURS;
  const to = Date.parse("2026-07-07T12:15:00.000Z");
  const from = to - seconds * count * 1000;
  return {
    scope: args.scope ?? "TRAIN",
    range,
    from: new Date(from).toISOString(),
    to: new Date(to).toISOString(),
    bucketSeconds: seconds,
    buckets: Array.from({ length: count }, (_, i) => {
      const completed = 3 + (i % 4);
      return {
        start: new Date(from + i * seconds * 1000).toISOString(),
        completed,
        failed: i % 7 === 0 ? 1 : 0,
        cancelled: i % 23 === 0 ? 1 : 0,
        p50DurationMs: 1200 + (i % 5) * 40,
        p95DurationMs: 2900 + (i % 5) * 120,
      };
    }),
  };
}

export const metricSeriesResolvers = {
  series: (_root: unknown, args: Args) => metricSeries(args),
};

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
    ManifestSummary: manifestScheduleResolvers,
    ManifestDetail: manifestScheduleResolvers,
    OperationsQueries: {
      manifests: manifestsResolver(),
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
    ManifestDetail: manifestScheduleResolvers,
    OperationsQueries: {
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
          inProgress: id === 1 ? 3 : 0,
          lastRun: "2026-07-07T11:00:00.000Z",
          // alpha is running; beta's latest ended run failed.
          lastEndedRunFailed: id === 2,
        })),
      // Global cross-group DAG: alpha (1) → beta (2).
      dependencyGraph: () => ({
        nodes: GROUP_ROWS.map((g) => ({ id: g.id, name: g.name, isHighlighted: false, isEnabled: g.isEnabled })),
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
          instanceId: "scheduler-2a9d4c8f0b",
          name: "trax-scheduler-1",
          environment: "kubernetes",
          traxVersion: "1.70.0",
          startedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
          lastHeartbeat: new Date(Date.now() - 5_000).toISOString(),
          online: true,
          runsDispatcher: true,
          runsManifestManager: true,
          workerSlots: 8,
          workerSlotsInUse: 3,
          currentlyRunning: 3,
        },
        {
          instanceId: "api-7f3c9a2b1e",
          name: "trax-api-1",
          environment: "kubernetes",
          traxVersion: "1.70.0",
          startedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
          lastHeartbeat: new Date(Date.now() - 8 * 60_000).toISOString(),
          online: false,
          runsDispatcher: false,
          runsManifestManager: false,
          workerSlots: null,
          workerSlotsInUse: null,
          currentlyRunning: 0,
        },
      ],
    },
    // Both scheduler loops switched on, so only the heartbeat decides the dispatcher warning.
    ConfigQueries: {
      scheduler: () => ({ manifestManagerEnabled: true, jobDispatcherEnabled: true }),
    },
    // The process serving the dashboard (the Hosts page's own panel).
    MetricsQueries: {
      server: () => ({
        processStartTimeUtc: new Date(Date.now() - 26 * 3600_000).toISOString(),
        uptimeSeconds: 26 * 3600 + 15 * 60,
        workingSetBytes: 412 * 1024 * 1024,
        gcHeapBytes: 96 * 1024 * 1024,
      }),
      serverCpuPercent: () => 4.2,
    },
  }),
};

// ── Train detail ─────────────────────────────────────────────────────────
// ZebraTrain declares 902's chain (trainChain). Its ten recent runs alternate, every third one
// failing at Summarize; three failed in the range at Summarize and one outside any step. A manifest
// (801's schedule) runs it, and the zebra-feeder machine's Feeding state invokes it. AlphaTrain has
// the same chain and has never run: no run, no manifest, no machine.
export const TRAIN_DETAIL_TRAIN = "Trax.Trains.ZebraTrain";
export const TRAIN_NEVER_RAN = "Trax.Trains.AlphaTrain";

const TRAIN_DETAIL_RUNS = Array.from({ length: 10 }, (_, i) => {
  const id = 960 - i;
  const state = i === 0 ? "IN_PROGRESS" : i % 3 === 1 ? "FAILED" : "COMPLETED";
  return { ...exec(id, TRAIN_DETAIL_TRAIN, state, i + 1), failureJunction: state === "FAILED" ? "Summarize" : null };
});

const TRAIN_DETAIL_FAILED = [
  ...TRAIN_DETAIL_RUNS.filter((r) => r.trainState === "FAILED"),
  { ...exec(930, TRAIN_DETAIL_TRAIN, "FAILED", 60), failureJunction: "TrainException" },
];

// One recent run of ZebraTrain on its chain: a failed run stopped at Summarize, a running one is in
// it, and a completed one took the Short track.
function trainDetailRunGraph(metadataId: number) {
  const run = TRAIN_DETAIL_RUNS.find((r) => r.id === metadataId);
  const before = new Set(["Fetch#0", "Findings#1"]);
  const stateOf = (id: string): RunNodeState => {
    if (!run) return "NOT_REACHED";
    if (run.trainState === "COMPLETED") return id === "Route#3/Long/Split#0" ? "SKIPPED" : "COMPLETED";
    if (before.has(id)) return "COMPLETED";
    if (id === "Summarize#2") return run.trainState === "FAILED" ? "FAILED" : "IN_PROGRESS";
    return "NOT_REACHED";
  };
  const chain = trainChain(TRAIN_DETAIL_TRAIN);
  return {
    ...chain,
    metadataId,
    allNodes: chain.allNodes.map((n) => ({ ...n, state: stateOf(n.id) })),
  };
}

const ZEBRA_FEEDER = {
  name: "zebra-feeder",
  version: 1,
  systemOwned: true,
  schemaHash: "feed".repeat(16),
  initialState: "Hungry",
  graphUnavailableReason: null,
  states: [
    { name: "Hungry", isInitial: true, isFinal: false, isCommitted: false, invokesTrain: null, spawnsMachine: null },
    { name: "Feeding", isInitial: false, isFinal: false, isCommitted: false, invokesTrain: TRAIN_DETAIL_TRAIN, spawnsMachine: null },
  ],
  transitions: [
    { from: "Hungry", command: "Feed", to: "Feeding", kind: "COMMAND", guard: null, effect: null, after: null },
    { from: "Feeding", command: "Feeding.done", to: "Hungry", kind: "OUTCOME", guard: null, effect: null, after: null },
  ],
  timers: [],
};

function trainDetailStats(trainName: string) {
  const ran = trainName === TRAIN_DETAIL_TRAIN;
  return {
    trainName,
    total: ran ? 420 : 0,
    completed: ran ? 400 : 0,
    failed: ran ? 15 : 0,
    inProgress: ran ? 2 : 0,
    pending: ran ? 3 : 0,
    cancelled: 0,
    lastRun: ran ? "2026-07-07T11:59:00.000Z" : null,
    lastSuccessfulRun: ran ? "2026-07-07T11:58:00.000Z" : null,
    averageMilliseconds: ran ? 1234.5 : null,
  };
}

export const trainDetailScenario: MockSchemaOverrides = {
  resolvers: () => ({
    ManifestSummary: manifestScheduleResolvers,
    ManifestDetail: manifestScheduleResolvers,
    MetricsQueries: metricSeriesResolvers,
    ...trainChainResolvers,
    OperationsQueries: {
      trains: () => TRAIN_ROWS,
      trainStats: (_root: unknown, args: Args) => trainDetailStats(args.trainName as string),
      executions: (_root: unknown, args: Args) => {
        if (args.trainName !== TRAIN_DETAIL_TRAIN) return pagedResult([]);
        return pagedResult(args.trainState === "FAILED" ? TRAIN_DETAIL_FAILED : TRAIN_DETAIL_RUNS);
      },
      runGraph: (_root: unknown, args: Args) => trainDetailRunGraph(args.metadataId as number),
      manifests: (_root: unknown, args: Args) =>
        pagedResult(
          args.nameContains === TRAIN_DETAIL_TRAIN
            ? [{ ...MANIFEST_ROWS[0], externalId: "zebra-every-5", name: TRAIN_DETAIL_TRAIN }]
            : [],
        ),
      manifestDetail: (_root: unknown, args: Args) => manifestDetail(args.id as number),
      machines: () => [...MACHINE_DEFINITIONS, ZEBRA_FEEDER],
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
// checkpoint, 954 failed without a saved input, 956 failed deep in its nested routing, 958 ran
// under a chain that has changed since.

// One node of a run graph; tracks and steps default to none.
function graphNode(id: string, kind: ChainStepKind, state: RunNodeState, fields: Partial<RunGraphNode> = {}): RunGraphNode {
  return {
    id,
    kind,
    junction: kind === "CHAIN" ? id.replace(/^.*\//, "").replace(/#\d+$/, "") : null,
    in: null,
    out: null,
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

// One step a node recorded, at its position in the run; a junction that completed unless told otherwise.
function graphStep(position: number, fields: Partial<RunGraphStep> = {}): RunGraphStep {
  return {
    position,
    kind: "JUNCTION",
    name: `Step${position}`,
    nameWithheld: false,
    state: "COMPLETED",
    startedAt: new Date(Date.parse("2026-07-07T11:55:00.000Z") + position * 2000).toISOString(),
    durationMs: 1500,
    attempt: null,
    failureClass: null,
    failureException: null,
    questionKey: null,
    answer: null,
    answerWithheld: false,
    confidence: null,
    decider: null,
    replayed: false,
    ...fields,
  };
}

const NO_GRAPH = { hasGraph: false, chainChanged: false, moreSteps: false, canResume: false, nodes: [], unmatchedSteps: [] };

// A run graph as a scenario declares it: the tree, from which runGraph serves the flat list too.
type DeclaredRunGraph = Omit<RunGraph, "allNodes"> & { nodes: RunGraphNode[] };

/** A run graph as the API serves it: the nested nodes and the flat allNodes the dashboard reads. */
export type ServedRunGraph = RunGraph & { nodes: RunGraphNode[] };

/** Serves a declared run graph as the API does, with every node at any depth in allNodes. */
export function servedRunGraph(graph: DeclaredRunGraph): ServedRunGraph {
  return { ...graph, allNodes: flattenRunGraph(graph.nodes) };
}

// One track holding one node, taken or not.
function oneNodeTrack(name: string, taken: boolean, node: RunGraphNode) {
  return { name, description: null, isFallback: false, taken, nodes: [node] };
}

/**
 * 956's train nests its routing deeper than a query can follow nested tracks: a Decide, a Switch
 * in it and another in that, a Parallel, a Gate in a branch and a Decide in the Gate, whose step six
 * tracks deep failed after the checkpoint at the top, so it can resume there. The other branch was
 * interrupted: the host stopped mid-junction.
 */
function deepRunGraph(metadataId: number): DeclaredRunGraph {
  const leaf = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0/Deep/Leaf#0", "CHAIN", "FAILED", {
    canResume: true,
    steps: [graphStep(4, { name: "Leaf", state: "FAILED", failureClass: "TRANSIENT", failureException: "TimeoutException" })],
  });
  const pick = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0", "DECIDE", "FAILED", {
    tracks: [oneNodeTrack("Deep", true, leaf)],
  });
  const check = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0", "GATE", "FAILED", {
    tracks: [oneNodeTrack("Open", true, pick)],
  });
  const right = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Right/Tally#0", "CHAIN", "INTERRUPTED");
  const lanes = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0", "PARALLEL", "FAILED", {
    tracks: [oneNodeTrack("Left", true, check), oneNodeTrack("Right", true, right)],
  });
  const score = graphNode("Plan#1/Fast/Mode#0/Lanes/Score#0", "SWITCH", "FAILED", {
    tracks: [oneNodeTrack("Lanes", true, lanes)],
  });
  const mode = graphNode("Plan#1/Fast/Mode#0", "SWITCH", "FAILED", {
    tracks: [oneNodeTrack("Lanes", true, score)],
  });
  return {
    metadataId,
    hasGraph: true,
    chainChanged: false,
    moreSteps: false,
    canResume: true,
    nodes: [
      graphNode("Saved#0", "CHECKPOINT", "COMPLETED", { checkpointed: true }),
      graphNode("Plan#1", "DECIDE", "FAILED", {
        tracks: [
          oneNodeTrack("Fast", true, mode),
          oneNodeTrack("Slow", false, graphNode("Plan#1/Slow/Wait#0", "CHAIN", "SKIPPED")),
        ],
      }),
    ],
    unmatchedSteps: [],
  };
}

/**
 * The run graph of a scenario run. 902 failed after its Findings checkpoint: the checkpoint is
 * stored, and it can resume at Summarize (the step after it, where it failed) or at Publish. 952 is
 * the run that resumed it there: the steps before are RESTORED. 954 (no saved input) has the same
 * checkpoint as 902, so its page draws "Resume from here" nowhere. 956 nests six levels deep (see
 * deepRunGraph). Every other run's train has no declared graph on this host.
 */
export function runGraph(metadataId: number): ServedRunGraph {
  return servedRunGraph(declaredRunGraph(metadataId));
}

function declaredRunGraph(metadataId: number): DeclaredRunGraph {
  if (metadataId === 956) return deepRunGraph(metadataId);
  if (metadataId === 902 || metadataId === 954) {
    return {
      metadataId,
      hasGraph: true,
      chainChanged: false,
      moreSteps: false,
      canResume: true,
      nodes: [
        graphNode("Fetch#0", "CHAIN", "COMPLETED", { in: "Order", out: "Order", steps: [graphStep(0, { name: "ValidateOrder", attempt: 2 })] }),
        graphNode("Findings#1", "CHECKPOINT", "COMPLETED", { checkpointed: true }),
        graphNode("Summarize#2", "CHAIN", "FAILED", {
          canResume: true,
          in: "Findings",
          out: "Summary",
          steps: [
            graphStep(2, {
              name: "ChargeCard",
              state: "FAILED",
              attempt: 2,
              failureClass: "TRANSIENT",
              failureException: "TimeoutException",
            }),
          ],
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
  if (metadataId === 952 || metadataId === 958) {
    return {
      metadataId,
      hasGraph: true,
      chainChanged: metadataId === 958,
      moreSteps: false,
      canResume: false,
      nodes: [
        graphNode("Fetch#0", "CHAIN", "RESTORED"),
        graphNode("Findings#1", "CHECKPOINT", "RESTORED", { checkpointed: true }),
        graphNode("Summarize#2", "CHAIN", "COMPLETED", { steps: [graphStep(0, { name: "Summarize" })] }),
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
    // 903 ran from a queued entry a machine's invoking state queued; 951 retried a dead letter.
    workQueueEntryId: id === 903 ? 504 : id === 951 ? 505 : null,
    deadLetterId: id === 951 ? 31 : null,
    invokedBy:
      id === 903 ? { machine: "source-partition", instanceId: MACHINE_IDS.dispatched, ownerKind: "SYSTEM", rowId: 11 } : null,
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
        if (id === 956) return execDetail(956, "Trax.Exec.NestedJob", "FAILED", 0);
        if (id === 958) return execDetail(958, "Trax.Exec.BetaJob", "COMPLETED", 0);
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

// A registered train's declared chain, as operations.trains { chain } serves it: the graph its runs
// are drawn on with no run, every node NOT_REACHED. Zebra and Alpha declare 902's chain (a Decide
// among it, so they ask decisions); Mango's chain cannot be read on this host.
const notReached = (node: RunGraphNode): RunGraphNode => ({
  ...node,
  state: "NOT_REACHED",
  replayed: false,
  checkpointed: false,
  canResume: false,
  steps: [],
  tracks: node.tracks.map((t) => ({ ...t, taken: false, nodes: t.nodes.map(notReached) })),
});

const hasDecide = (nodes: RunGraphNode[]): boolean =>
  nodes.some((n) => n.kind === "DECIDE" || n.tracks.some((t) => hasDecide(t.nodes)));

/** A train's declared chain with no run, keyed by its full name. */
export function trainChain(fullName: string) {
  const declared = fullName === "Trax.Trains.MangoTrain" ? { ...NO_GRAPH, metadataId: 0 } : declaredRunGraph(902);
  return {
    ...servedRunGraph({ ...declared, metadataId: 0, canResume: false, nodes: declared.nodes.map(notReached) }),
    train: fullName,
    hash: null,
  };
}

/** The TrainInfo resolvers: each train's declared chain and whether it asks a decider. */
export const trainChainResolvers = {
  TrainInfo: {
    chain: (train: { fullName: string }) => trainChain(train.fullName),
    asksDecisions: (train: { fullName: string }) => hasDecide(trainChain(train.fullName).nodes),
  },
};

export const trainsScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: { trains: () => TRAIN_ROWS },
    ...trainChainResolvers,
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
          { id: (args.groupId as number) ?? 1, name: "alpha-group", isHighlighted: true, isEnabled: true },
          { id: 2, name: "beta-group", isHighlighted: false, isEnabled: false },
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
          inProgress: id === 2 ? 0 : 10,
          lastRun: "2026-07-07T11:00:00.000Z",
          lastEndedRunFailed: id === 2,
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

/** A host that does not serve persisted-operation management, or a caller outside its gate: the probe fails. */
export const persistedOperationsUnavailable: MockSchemaOverrides = errorOverride(
  "RootQuery",
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
  attention: { dueAt?: string; overdue?: boolean; stuck?: boolean; waitingOn?: { executionId: number | null; trainState: string | null; workQueueEntryId: number | null } } = {},
) {
  const updatedAt = `2026-07-07T11:${String(minute).padStart(2, "0")}:00.000Z`;
  return {
    machine,
    ownerKind,
    id,
    rowId,
    state,
    version: 1,
    createdAt: "2026-07-07T10:00:00.000Z",
    updatedAt,
    hasLiveInvokedRun,
    // No transition log yet: the time it entered its state is when it last changed.
    inStateSince: updatedAt,
    dueAt: attention.dueAt ?? null,
    overdue: attention.overdue ?? false,
    stuck: attention.stuck ?? false,
    waitingOn: attention.waitingOn ?? null,
  };
}

export const MACHINE_ROWS = [
  machineRow("source-partition", "SYSTEM", MACHINE_IDS.dispatched, 11, "Ingesting", true, 58, {
    waitingOn: { executionId: 7101, trainState: "IN_PROGRESS", workQueueEntryId: 7191 },
  }),
  machineRow("topic-map", "USER", MACHINE_IDS.draftBuilding, 21, "Building", true, 57, {
    waitingOn: { executionId: 7110, trainState: "IN_PROGRESS", workQueueEntryId: 7192 },
  }),
  machineRow("source-partition", "SYSTEM", MACHINE_IDS.queued, 12, "Ingesting", true, 56, {
    waitingOn: { executionId: null, trainState: "PENDING", workQueueEntryId: 7201 },
  }),
  // Its 24-hour timer was due an hour ago and nothing fired it: overdue, so stuck.
  machineRow("topic-map", "USER", MACHINE_IDS.draftIdle, 22, "ChoosingRange", false, 55, {
    dueAt: "2026-07-07T10:55:00.000Z",
    overdue: true,
    stuck: true,
  }),
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
  const counts = new Map<string, { machine: string; state: string; ownerKind: string; count: number; stuck: number }>();
  for (const r of rows) {
    const key = `${r.machine}|${r.state}|${r.ownerKind}`;
    const c = counts.get(key) ?? { machine: r.machine, state: r.state, ownerKind: r.ownerKind, count: 0, stuck: 0 };
    c.count++;
    if (r.stuck) c.stuck++;
    counts.set(key, c);
  }
  return [...counts.values()];
}

function machineInstancesPage(args: Args, capped = false) {
  let rows = MACHINE_ROWS;
  if (args.machine != null) rows = rows.filter((r) => r.machine === args.machine);
  if (args.state != null) rows = rows.filter((r) => r.state === args.state);
  if (args.ownerKind != null) rows = rows.filter((r) => r.ownerKind === args.ownerKind);
  if (args.stuck != null) rows = rows.filter((r) => r.stuck === args.stuck);
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

// The machines the host registers, as operations.machines describes them: the two the instances
// above belong to, and one with no instance at all, which only this read lists.
function state(name: string, marks: Partial<Record<"isInitial" | "isFinal" | "isCommitted", boolean>> = {}, invokesTrain: string | null = null) {
  return { name, isInitial: false, isFinal: false, isCommitted: false, invokesTrain, spawnsMachine: null, ...marks };
}

function edge(from: string, command: string, to: string, kind: "COMMAND" | "TIMER" | "OUTCOME", extra: { guard?: string; effect?: string; after?: string } = {}) {
  return { from, command, to, kind, guard: extra.guard ?? null, effect: extra.effect ?? null, after: extra.after ?? null };
}

export const MACHINE_DEFINITIONS = [
  {
    name: "retention-sweep",
    version: 1,
    systemOwned: true,
    schemaHash: "c0ffee".repeat(10) + "beef",
    initialState: "Idle",
    graphUnavailableReason: null,
    states: [state("Idle", { isInitial: true }), state("Sweeping", {}, "Trax.Samples.Recovery.ISweepRetentionTrain")],
    transitions: [
      edge("Idle", "Idle.after", "Sweeping", "TIMER", { after: "PT1H" }),
      edge("Sweeping", "Sweeping.done", "Idle", "OUTCOME"),
      edge("Sweeping", "Sweeping.failed", "Idle", "OUTCOME"),
      edge("Sweeping", "Sweeping.cancelled", "Idle", "OUTCOME"),
    ],
    timers: [{ state: "Idle", command: "Idle.after", target: "Sweeping", after: "PT1H", field: null, min: null, max: null }],
  },
  {
    name: "source-partition",
    version: 1,
    systemOwned: true,
    schemaHash: "a1b2c3d4".repeat(8),
    initialState: "Pending",
    graphUnavailableReason: null,
    states: [
      state("Failed"),
      state("Ingested", { isFinal: true, isCommitted: true }),
      state("Ingesting", {}, "Trax.Samples.Recovery.IIngestPartitionTrain"),
      state("Pending", { isInitial: true }),
    ],
    transitions: [
      edge("Failed", "Retry", "Ingesting", "COMMAND"),
      edge("Pending", "Ingest", "Ingesting", "COMMAND"),
      edge("Ingesting", "Ingesting.done", "Ingested", "OUTCOME"),
      edge("Ingesting", "Ingesting.failed", "Failed", "OUTCOME"),
      edge("Ingesting", "Ingesting.cancelled", "Failed", "OUTCOME"),
    ],
    timers: [],
  },
  {
    name: "topic-map",
    version: 2,
    systemOwned: false,
    schemaHash: "0f1e2d3c".repeat(8),
    initialState: "ChoosingRange",
    graphUnavailableReason: null,
    states: [
      state("Building", {}, "Trax.Samples.Recovery.IBuildTopicMapTrain"),
      state("Built", { isCommitted: true }),
      state("ChoosingRange", { isInitial: true }),
      state("Expired", { isFinal: true }),
    ],
    transitions: [
      edge("Built", "Rebuild", "ChoosingRange", "COMMAND"),
      edge("ChoosingRange", "Build", "Building", "COMMAND", { guard: "Choose a range of at least one day" }),
      edge("ChoosingRange", "ChoosingRange.after", "Expired", "TIMER", { after: "PT24H" }),
      edge("Building", "Building.done", "Built", "OUTCOME"),
      edge("Building", "Building.failed", "ChoosingRange", "OUTCOME"),
      edge("Building", "Building.cancelled", "ChoosingRange", "OUTCOME"),
    ],
    timers: [{ state: "ChoosingRange", command: "ChoosingRange.after", target: "Expired", after: "PT24H", field: null, min: null, max: null }],
  },
];

// Each instance's history, newest first, as machineInstance { transitions } pages it. ...0001 has
// retried its ingest many times, so its history takes more than one page; the others are short.
type TransitionRow = {
  revision: number;
  kind: "START" | "COMMAND" | "SAVE" | "TIMER" | "OUTCOME";
  trigger: string | null;
  actor: "OWNER" | "HOST" | "MACHINE";
  fromState: string | null;
  toState: string;
  at: string;
  previousAt: string | null;
  startedRun: { executionId: number | null; trainState: string | null; workQueueEntryId: number | null } | null;
  startedChild: { machine: string; ownerKind: string; id: string; rowId: number } | null;
};

const transitionAt = (minutesAfterTen: number) => new Date(Date.UTC(2026, 6, 7, 10, 0, 0) + minutesAfterTen * 60_000).toISOString();

/** Builds a history oldest first, then serves it newest first with each one's previousAt. */
function transitionHistory(steps: Omit<TransitionRow, "revision" | "previousAt">[]): TransitionRow[] {
  return steps
    .map((step, i) => ({ ...step, revision: i + 1, previousAt: i === 0 ? null : steps[i - 1].at }))
    .reverse();
}

const transitionStep = (
  kind: TransitionRow["kind"],
  trigger: string | null,
  actor: TransitionRow["actor"],
  fromState: string | null,
  toState: string,
  minute: number,
  startedRun: TransitionRow["startedRun"] = null,
): Omit<TransitionRow, "revision" | "previousAt"> => ({ kind, trigger, actor, fromState, toState, at: transitionAt(minute), startedRun, startedChild: null });

function retries(): Omit<TransitionRow, "revision" | "previousAt">[] {
  const steps = [
    transitionStep("START", null, "HOST", null, "Pending", 0),
    transitionStep("COMMAND", "Ingest", "HOST", "Pending", "Ingesting", 1, { executionId: 7001, trainState: "FAILED", workQueueEntryId: 7151 }),
  ];
  for (let i = 0; i < 18; i++) {
    steps.push(transitionStep("OUTCOME", "Ingesting.failed", "MACHINE", "Ingesting", "Failed", 2 + i * 3));
    const run = i === 17 ? { executionId: 7101, trainState: "IN_PROGRESS", workQueueEntryId: 7191 } : { executionId: 7002 + i, trainState: "FAILED", workQueueEntryId: 7152 + i };
    steps.push(transitionStep("COMMAND", "Retry", "HOST", "Failed", "Ingesting", 3 + i * 3, run));
  }
  return steps;
}

export const MACHINE_TRANSITIONS: Record<string, TransitionRow[]> = {
  [MACHINE_IDS.dispatched]: transitionHistory(retries()),
  [MACHINE_IDS.queued]: transitionHistory([
    transitionStep("START", null, "HOST", null, "Pending", 50),
    transitionStep("COMMAND", "Ingest", "HOST", "Pending", "Ingesting", 56, { executionId: null, trainState: "PENDING", workQueueEntryId: 7201 }),
  ]),
  [MACHINE_IDS.idle]: transitionHistory([
    transitionStep("START", null, "HOST", null, "Pending", 20),
    transitionStep("COMMAND", "Ingest", "HOST", "Pending", "Ingesting", 30, { executionId: 7050, trainState: "COMPLETED", workQueueEntryId: 7140 }),
    transitionStep("OUTCOME", "Ingesting.done", "MACHINE", "Ingesting", "Ingested", 54),
  ]),
  [MACHINE_IDS.draftBuilding]: transitionHistory([
    transitionStep("START", null, "OWNER", null, "ChoosingRange", 10),
    transitionStep("SAVE", null, "OWNER", "ChoosingRange", "ChoosingRange", 12),
    transitionStep("COMMAND", "Build", "OWNER", "ChoosingRange", "Building", 20, { executionId: 7100, trainState: "FAILED", workQueueEntryId: 7180 }),
    transitionStep("OUTCOME", "Building.failed", "MACHINE", "Building", "ChoosingRange", 31),
    transitionStep("COMMAND", "Build", "OWNER", "ChoosingRange", "Building", 117, { executionId: 7110, trainState: "IN_PROGRESS", workQueueEntryId: 7192 }),
  ]),
  [MACHINE_IDS.draftIdle]: transitionHistory([
    transitionStep("START", null, "OWNER", null, "ChoosingRange", 5),
    transitionStep("SAVE", null, "OWNER", "ChoosingRange", "ChoosingRange", 115),
  ]),
};

/** One page of an instance's history, newest first, older than `after` when given. */
export function machineTransitionsPage(instanceId: string, args: Args) {
  const all = MACHINE_TRANSITIONS[instanceId] ?? [];
  const first = Math.min(Math.max(Number(args.first ?? 25), 1), 500);
  const older = args.after == null ? all : all.filter((t) => t.revision < Number(args.after));
  const items = older.slice(0, first);
  return { items, nextCursor: older.length > first ? items[items.length - 1].revision : null };
}

/** The MachineInstanceDetail resolvers of {@link machineScenario}: its history, read when selected. */
export const machineInstanceDetailResolvers = {
  MachineInstanceDetail: {
    transitions: (root: { id: string }, args: Args) => machineTransitionsPage(root.id, args),
  },
};

/** The OperationsQueries resolvers of {@link machineScenario}. */
export const machineQueryResolvers = {
  machineInstanceCounts: (_root: unknown, args: Args) =>
    machineCounts(args.machine != null ? MACHINE_ROWS.filter((r) => r.machine === args.machine) : MACHINE_ROWS),
  machineInstances: (_root: unknown, args: Args) => machineInstancesPage(args),
  machineInstance: (_root: unknown, args: Args) => machineInstanceDetail(args),
  machines: () => MACHINE_DEFINITIONS,
};

export const machineScenario: MockSchemaOverrides = {
  resolvers: () => ({ OperationsQueries: machineQueryResolvers, ...machineInstanceDetailResolvers }),
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

// ── Lookup ─────────────────────────────────────────────────────────────────
// operations.lookup over the scenarios' rows: exact matches only, as the API matches. A number names
// a scenario run; a GUID an instance (a user's draft carries its rowId, never whose it is); a name a
// registered train.
const SCENARIO_RUNS = [800, 900, 902, 903, 950, 951, 952, 954, 956];

export function lookupMatches(args: Args) {
  const text = String(args.text ?? "").trim();
  const none = { machine: null, ownerKind: null, rowId: null };
  const matches: Record<string, unknown>[] = [];
  if (/^\d+$/.test(text) && SCENARIO_RUNS.includes(Number(text))) {
    matches.push({ kind: "RUN", id: text, label: `Run ${text}`, matchedOn: "id", ...none });
  }
  for (const row of MACHINE_ROWS)
    if (row.id === text)
      matches.push({
        kind: "MACHINE_INSTANCE",
        id: row.id,
        label: `${row.machine} · ${row.state}`,
        matchedOn: "id",
        machine: row.machine,
        ownerKind: row.ownerKind,
        rowId: row.rowId,
      });
  for (const train of TRAIN_ROWS)
    if (train.fullName === text || train.serviceTypeName === text)
      matches.push({ kind: "TRAIN", id: train.fullName, label: train.serviceTypeName, matchedOn: "name", ...none });
  return matches;
}

export const lookupScenario: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: { lookup: (_root: unknown, args: Args) => lookupMatches(args) },
  }),
};

// ── Bulk operations ──────────────────────────────────────────────────────
// One running cancel with failures of two reasons, one finished acknowledge, one that stopped on a
// refused batch. Served by an overlay (the default one reads only what the session started), so a
// story passes `overlays: [bulkOperationsOverlay]`.
const EMPTY_BULK_FILTER = { executions: null, deadLetters: null, manifests: null };
const BULK_EXECUTION_FILTER = {
  trainState: "IN_PROGRESS", trainName: "Trax.Demo.Trains.ImportTrain", startedAfter: "2026-07-07T02:00:00.000Z",
  startedBefore: null, manifestId: null, manifestGroupId: null, hideAdminTrains: true, failureClass: null,
  externalId: null, parentId: null, hostName: null, failureReasonContains: null, failureJunction: null,
};
const BULK_FAILURES = [
  ...Array.from({ length: 9 }, (_, i) => ({ id: 88_100 + i, reason: "It had already ended (completed)." })),
  ...Array.from({ length: 3 }, (_, i) => ({ id: 88_200 + i, reason: "It no longer exists." })),
];
export const BULK_OPERATION_IDS = {
  running: "0f8e2c1a-6d1b-4c3e-9a57-1b2d3c4e5f60",
  finished: "1a2b3c4d-0000-4000-9000-000000000002",
  failed: "1a2b3c4d-0000-4000-9000-000000000003",
};
export const BULK_OPERATION_ROWS = [
  {
    id: BULK_OPERATION_IDS.running, kind: "CANCEL", status: "RUNNING", actor: "apikey:operator", askAfresh: false,
    startedAt: "2026-07-07T11:58:00.000Z", finishedAt: null, total: 312, done: 180, failed: 12,
    message: "192 of 312 attempted; 12 did not take.",
    filter: { ...EMPTY_BULK_FILTER, executions: BULK_EXECUTION_FILTER },
    failureReasons: [
      { reason: "It had already ended (completed).", count: 9 },
      { reason: "It no longer exists.", count: 3 },
    ],
    failures: BULK_FAILURES,
  },
  {
    id: BULK_OPERATION_IDS.finished, kind: "ACKNOWLEDGE", status: "FINISHED", actor: "oidc:ops@example.com", askAfresh: false,
    startedAt: "2026-07-07T10:00:00.000Z", finishedAt: "2026-07-07T10:00:04.000Z", total: 40, done: 40, failed: 0,
    message: "ACKNOWLEDGE applied to 40 of 40; 0 did not take.",
    filter: { ...EMPTY_BULK_FILTER, deadLetters: { status: "AWAITING_INTERVENTION", manifestId: 41 } },
    failureReasons: [], failures: [],
  },
  {
    id: BULK_OPERATION_IDS.failed, kind: "TRIGGER", status: "FAILED", actor: "apikey:operator", askAfresh: true,
    startedAt: "2026-07-07T09:00:00.000Z", finishedAt: "2026-07-07T09:00:02.000Z", total: 10, done: 4, failed: 1,
    message: "Stopped after 5 of 10: the server refused a batch.",
    filter: { ...EMPTY_BULK_FILTER, manifests: { isEnabled: true, scheduleType: "CRON", nameContains: "Order", manifestGroupId: null, hideAdminTrains: true } },
    failureReasons: [{ reason: "It no longer exists.", count: 1 }],
    failures: [{ id: 1_000_001, reason: "It no longer exists." }],
  },
];

// What the list reads of each: everything but the filter and the failures.
function bulkSummary(row: (typeof BULK_OPERATION_ROWS)[number]) {
  const { id, kind, status, actor, askAfresh, startedAt, finishedAt, total, done, failed, message } = row;
  return { id, kind, status, actor, askAfresh, startedAt, finishedAt, total, done, failed, message };
}

export const bulkOperationsOverlay: StatefulOverlay = {
  queries: {
    BulkOperations: (data) => ({
      ...(data as Record<string, unknown>),
      operations: { bulkOperations: BULK_OPERATION_ROWS.map(bulkSummary) },
    }),
    BulkOperation: (data, _store, variables) => {
      const row = BULK_OPERATION_ROWS.find((r) => r.id === variables.id);
      const after = (variables.failuresAfter as number | null | undefined) ?? null;
      const take = (variables.failuresTake as number | undefined) ?? 100;
      return {
        ...(data as Record<string, unknown>),
        operations: {
          bulkOperation: row
            ? { ...row, failures: row.failures.filter((f) => after == null || f.id > after).slice(0, take) }
            : null,
        },
      };
    },
  },
};

// ── Attention ──────────────────────────────────────────────────────────────
// operations.attention as the API answers it: each entry's link is the console path and query that lists it.
const ATTENTION_AS_OF = "2026-07-07T12:00:00.000Z";

/** Nothing needs attention: the scheduler runs, nothing is dead-lettered, stuck, backed up or offline. */
export const attentionAllClear = {
  asOf: ATTENTION_AS_OF,
  scheduler: { manifestManagerEnabled: true, jobDispatcherEnabled: true, paused: false, link: "/system/scheduler" },
  deadLetters: [],
  failingTrains: [],
  stuckInstances: [],
  queue: { queued: 0, oldestQueuedAt: null, oldestAgeSeconds: null, link: "/runs/queued?status=QUEUED" },
  offlineHosts: [],
};

/** A paused dispatcher, two dead letters sharing a train and reason, a failing train, stuck instances, a backlog and an offline host. */
export const attentionNeedsWork = {
  asOf: ATTENTION_AS_OF,
  scheduler: { manifestManagerEnabled: true, jobDispatcherEnabled: false, paused: true, link: "/system/scheduler" },
  deadLetters: [
    {
      trainName: "Trax.Samples.Billing.IChargeCardTrain",
      reason: "Max retries exceeded: (4) failures > (3) max retries",
      count: 2,
      oldestDeadLetteredAt: "2026-07-07T09:30:00.000Z",
      link:
        "/runs/dead-letters?status=AWAITING_INTERVENTION&train=Trax.Samples.Billing.IChargeCardTrain" +
        "&reason=Max%20retries%20exceeded%3A%20(4)%20failures%20%3E%20(3)%20max%20retries",
    },
    {
      trainName: "Trax.Samples.Mail.ISendDigestTrain",
      reason: "Max retries exceeded: (2) failures > (1) max retries",
      count: 1,
      oldestDeadLetteredAt: "2026-07-07T11:10:00.000Z",
      link:
        "/runs/dead-letters?status=AWAITING_INTERVENTION&train=Trax.Samples.Mail.ISendDigestTrain" +
        "&reason=Max%20retries%20exceeded%3A%20(2)%20failures%20%3E%20(1)%20max%20retries",
    },
  ],
  failingTrains: [
    {
      trainName: "Trax.Samples.Billing.IChargeCardTrain",
      failedLastHour: 6,
      finishedLastHour: 10,
      lastHourFailureRate: 0.6,
      baselineFailureRate: 0.04,
      link: "/runs?state=FAILED&train=Trax.Samples.Billing.IChargeCardTrain",
    },
  ],
  stuckInstances: [
    { machine: "order", state: "AwaitingPayment", count: 3, link: "/machines/order?state=AwaitingPayment&stuck=true" },
  ],
  queue: {
    queued: 42,
    oldestQueuedAt: "2026-07-07T11:48:00.000Z",
    oldestAgeSeconds: 720,
    link: "/runs/queued?status=QUEUED",
  },
  offlineHosts: [
    {
      instanceId: "worker-7c41d2",
      hostName: "trax-worker-2",
      lastHeartbeatAt: "2026-07-07T11:20:00.000Z",
      runsDispatcher: false,
      link: "/system/hosts",
    },
  ],
};

export function attentionScenarioOf(attention: typeof attentionAllClear | typeof attentionNeedsWork): MockSchemaOverrides {
  return { resolvers: () => ({ OperationsQueries: { attention: () => attention } }) };
}

export const attentionScenario = attentionScenarioOf(attentionNeedsWork);

// ── Overview ───────────────────────────────────────────────────────────────
// The Overview's reads: health and the metrics block, either with a day of runs or with none (a
// new host), plus what needs attention.
const OVERVIEW_HOURS = Array.from({ length: 24 }, (_, i) => `2026-07-07T${String(i).padStart(2, "0")}:00:00.000Z`);

function overviewMetrics(runs: boolean) {
  return {
    kpis: runs
      ? { executionsToday: 312, successRate: 96.2, currentlyRunning: 3, unresolvedDeadLetters: 3 }
      : { executionsToday: 0, successRate: 0, currentlyRunning: 0, unresolvedDeadLetters: 0 },
    executionsOverTime: OVERVIEW_HOURS.map((timestamp, i) => ({
      timestamp,
      completed: runs ? 10 + (i % 5) : 0,
      failed: runs ? i % 3 : 0,
      cancelled: 0,
    })),
    topFailures: runs ? [{ trainName: "Trax.Samples.Billing.IChargeCardTrain", count: 9 }] : [],
    topAverageDurations: runs ? [{ trainName: "Trax.Samples.Billing.IChargeCardTrain", averageMilliseconds: 840 }] : [],
    throughputSeries: runs
      ? [
          {
            trainName: "Trax.Samples.Billing.IChargeCardTrain",
            buckets: OVERVIEW_HOURS.map((timestamp, i) => ({ timestamp, count: 10 + (i % 5) })),
          },
        ]
      : [],
  };
}

export function overviewScenario(
  attention: typeof attentionAllClear | typeof attentionNeedsWork,
  { runs }: { runs: boolean },
): MockSchemaOverrides {
  return {
    resolvers: () => ({
      OperationsQueries: {
        attention: () => attention,
        health: () => ({
          status: runs ? "Degraded" : "Healthy",
          description: runs ? "3 dead letters await intervention." : "No problems found.",
          queueDepth: runs ? 42 : 0,
          inProgress: runs ? 3 : 0,
          failedLastHour: runs ? 6 : 0,
          deadLetters: runs ? 3 : 0,
        }),
      },
      MetricsQueries: {
        dashboard: () => overviewMetrics(runs),
        server: () => ({ uptimeSeconds: 3600, workingSetBytes: 200_000_000, gcHeapBytes: 50_000_000 }),
        serverCpuPercent: () => 2.5,
      },
    }),
  };
}
