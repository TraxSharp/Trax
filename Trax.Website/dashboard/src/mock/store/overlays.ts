import type { DocumentNode } from "graphql";
import { MACHINE_INSTANCE } from "../../graphql/queries";
import type { MockStore } from "./mock-store";

// An overlay teaches the mock how a slice of data is written by mutations and read back by
// queries, both routed through the MockStore delta. The store and the exchange stay generic;
// overlays are the only place that knows the GraphQL shapes.
//
//   mutations[opName] runs when that mutation fires: mutate the store, return the response
//     data the client should see (a realistic ACK, not the auto-mocker's random payload).
//   queries[opName] runs after the base result (fixture / auto-mock): merge the store delta.
//   subscriptions lists the subscription op names this mock drives via store.publishEvent.

export type MutationOverlay = (
  variables: Record<string, unknown>,
  store: MockStore,
  context: MutationContext,
) => Record<string, unknown> | Promise<Record<string, unknown>>;

/**
 * What a mutation overlay can ask of the mock besides its store: a read through the same client, for
 * a write whose answer depends on rows its variables do not carry. The read is answered as a page's
 * would be (base data, then the overlays), so the answer never depends on what a page read first.
 */
export interface MutationContext {
  query(document: DocumentNode, variables: Record<string, unknown>): Promise<unknown>;
}

export type QueryOverlay = (
  data: unknown,
  store: MockStore,
  variables: Record<string, unknown>,
) => unknown;

export interface StatefulOverlay {
  mutations?: Record<string, MutationOverlay>;
  queries?: Record<string, QueryOverlay>;
  subscriptions?: string[];
}

type Rec = Record<string, unknown>;

function asRecord(value: unknown): Rec | undefined {
  return value && typeof value === "object" && !Array.isArray(value) ? (value as Rec) : undefined;
}

// Build a nested response object from a dot path: wrap("a.b.c", payload) -> {a:{b:{c:payload}}}.
function wrap(path: string, payload: Rec): Rec {
  return path.split(".").reduceRight<Rec>((acc, key) => ({ [key]: acc }), payload as Rec);
}

// Immutably replace the array found at `path` (dot path to the items array).
function updateItemsAtPath(data: unknown, path: string, fn: (items: unknown[]) => unknown[]): unknown {
  const keys = path.split(".");
  function recur(obj: unknown, i: number): unknown {
    const rec = asRecord(obj);
    if (!rec) return obj;
    const key = keys[i];
    if (i === keys.length - 1) {
      const arr = rec[key];
      return Array.isArray(arr) ? { ...rec, [key]: fn(arr) } : rec;
    }
    return { ...rec, [key]: recur(rec[key], i + 1) };
  }
  return recur(data, 0);
}

// Immutably map each item of the array found at `path`.
function mapItemsAtPath(data: unknown, path: string, fn: (item: unknown) => unknown): unknown {
  return updateItemsAtPath(data, path, (items) => items.map(fn));
}

// Immutably merge `patch` into the object found at `path` (dot path to the object).
function patchObjectAtPath(data: unknown, path: string, patch: Rec): unknown {
  const keys = path.split(".");
  function recur(obj: unknown, i: number): unknown {
    const rec = asRecord(obj);
    if (!rec) return obj;
    const key = keys[i];
    if (i === keys.length - 1) {
      const target = asRecord(rec[key]);
      return target ? { ...rec, [key]: { ...target, ...patch } } : rec;
    }
    return { ...rec, [key]: recur(rec[key], i + 1) };
  }
  return recur(data, 0);
}

function readDelta<T>(store: MockStore, key: string, empty: T): T {
  const raw = store.getState()[key];
  return (raw as T) ?? empty;
}

// The timestamp for "resolved just now" fields. Fixed by default, so stories and tests stay
// deterministic; the demo build reads the real clock instead, since it moves every recorded time to
// the visitor's present.
let clock = (): string => "2026-07-07T12:00:00.000Z";
export function setOverlayClock(now: () => string): void {
  clock = now;
}
const nowIso = (): string => clock();

// ── Work queue ─────────────────────────────────────────────────────────────
// CancelWorkQueueEntries / CancelWorkQueueEntry -> WorkQueue / WorkQueueDetail (rows CANCELLED).

const CANCELLED_KEY = "workQueueCancelledIds";

function addCancelled(store: MockStore, ids: number[]) {
  store.update("CancelWorkQueue", (draft) => {
    const current = (draft[CANCELLED_KEY] as number[] | undefined) ?? [];
    draft[CANCELLED_KEY] = [...new Set([...current, ...ids])];
  });
}

const cancelWorkQueueEntries: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  addCancelled(store, ids);
  return wrap("operations.workQueue.cancelWorkQueueEntries", {
    success: true,
    count: ids.length,
    message: `${ids.length} work queue entry(s) cancelled.`,
  });
};

const cancelWorkQueueEntry: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  addCancelled(store, [id]);
  return wrap("operations.workQueue.cancelWorkQueueEntry", {
    success: true,
    message: `Entry #${id} cancelled.`,
  });
};

// The ids the session hands out (a queued entry, a run, a history row), each sequence kept in the
// store rather than the module, so every store (a story, a test, a tab) counts from the same first
// id whatever ran before it, and a store restored from storage carries on past the ids it holds.
const ID_SEQUENCES_KEY = "idSequences";

function takeId(draft: Record<string, unknown>, sequence: string, first: number): number {
  const sequences = { ...(draft[ID_SEQUENCES_KEY] as Record<string, number> | undefined) };
  const id = sequences[sequence] ?? first;
  sequences[sequence] = id + 1;
  draft[ID_SEQUENCES_KEY] = sequences;
  return id;
}

function nextId(store: MockStore, sequence: string, first: number): number {
  let id = first;
  store.update("NextId", (draft) => {
    id = takeId(draft, sequence, first);
  });
  return id;
}

// Entries the session queued (queueTrain / requeueExecution), keyed by id, so the list shows
// them and the detail page the queue dialog navigates to resolves them.
const QUEUED_KEY = "workQueueQueued";
const nextQueuedId = (store: MockStore) => nextId(store, "workQueue", 9_100_000);

function queuedEntry(id: number, trainName: string, priority: number, input: string | null): Rec {
  return {
    id,
    externalId: `wq-mock-${id}`,
    trainName,
    status: "QUEUED",
    createdAt: nowIso(),
    dispatchedAt: null,
    scheduledAt: null,
    priority,
    dispatchAttempts: 0,
    manifestId: null,
    metadataId: null,
    deadLetterId: null,
    inputTypeName: null,
    confirmedAt: nowIso(),
    subjectKey: null,
    input,
    subjectHeldBy: null,
    subjectQueuedBehind: null,
  };
}

function addQueued(store: MockStore, action: string, entry: Rec) {
  store.update(action, (draft) => {
    draft[QUEUED_KEY] = {
      ...(draft[QUEUED_KEY] as Record<string, Rec> | undefined),
      [String(entry.id)]: entry,
    };
  });
}

// The server refuses input it cannot read as JSON with success: false and a message; the mock
// does the same so the dialogs' refusal path can be exercised.
function invalidJson(inputJson: unknown): string | null {
  if (inputJson == null || inputJson === "") return null;
  try {
    JSON.parse(String(inputJson));
    return null;
  } catch {
    return "The input is not valid JSON.";
  }
}

const queueTrain: MutationOverlay = (variables, store) => {
  const input = asRecord(variables.input) ?? {};
  const refusal = invalidJson(input.inputJson);
  if (refusal) return wrap("operations.workQueue.queueTrain", { success: false, message: refusal, id: null });
  const id = nextQueuedId(store);
  addQueued(
    store,
    "QueueTrain",
    queuedEntry(id, String(input.trainName ?? ""), Number(input.priority ?? 0), (input.inputJson as string) ?? null),
  );
  return wrap("operations.workQueue.queueTrain", { success: true, message: "Train queued.", id });
};

// Runs started with runTrain, keyed by id: the execution detail page resolves them.
const RUN_KEY = "runTrainRuns";

const runTrain: MutationOverlay = (variables, store) => {
  const input = asRecord(variables.input) ?? {};
  const refusal = invalidJson(input.inputJson);
  if (refusal) return wrap("operations.workQueue.runTrain", { success: false, message: refusal, id: null });
  const id = nextId(store, "run", 9_200_000);
  store.update("RunTrain", (draft) => {
    draft[RUN_KEY] = {
      ...(draft[RUN_KEY] as Record<string, Rec> | undefined),
      [String(id)]: {
        id,
        externalId: `run-mock-${id}`,
        name: String(input.trainName ?? ""),
        trainState: "COMPLETED",
        startTime: nowIso(),
        endTime: nowIso(),
        input: (input.inputJson as string) ?? null,
      },
    };
  });
  return wrap("operations.workQueue.runTrain", { success: true, message: "Train ran.", id });
};

const readWorkQueue: QueryOverlay = (data, store, variables) => {
  const cancelled = new Set(readDelta<number[]>(store, CANCELLED_KEY, []));
  const queued = Object.values(readDelta<Record<string, Rec>>(store, QUEUED_KEY, {}));
  if (cancelled.size === 0 && queued.length === 0) return data;
  const patched = mapItemsAtPath(data, "operations.workQueue.workQueues.items", (item) => {
    const r = asRecord(item);
    return r && cancelled.has(r.id as number) ? { ...r, status: "CANCELLED" } : item;
  });
  // Newly queued entries lead the first page of a view that would include them.
  const status = variables.status as string | undefined;
  const trainName = (variables.trainName as string | undefined)?.toLowerCase();
  const subjectKey = variables.subjectKey as string | null | undefined;
  const manifestId = variables.manifestId as number | null | undefined;
  const fresh = queued
    .filter(
      (e) =>
        (!status || status === "QUEUED") &&
        (!trainName || String(e.trainName).toLowerCase().includes(trainName)) &&
        (subjectKey == null || e.subjectKey === subjectKey) &&
        (manifestId == null || e.manifestId === manifestId),
    )
    .map((e) => (cancelled.has(e.id as number) ? { ...e, status: "CANCELLED" } : e));
  if (variables.afterId != null || fresh.length === 0) return patched;
  const ids = new Set(fresh.map((e) => e.id));
  return updateItemsAtPath(patched, "operations.workQueue.workQueues.items", (items) => [
    ...fresh,
    ...items.filter((i) => !ids.has(asRecord(i)?.id)),
  ]);
};

const readWorkQueueDetail: QueryOverlay = (data, store, variables) => {
  const id = variables.id as number;
  const queued = readDelta<Record<string, Rec>>(store, QUEUED_KEY, {})[String(id)];
  const cancelled = new Set(readDelta<number[]>(store, CANCELLED_KEY, [])).has(id);
  let out = data;
  if (queued) out = wrap("operations.workQueue.detail", { ...queued });
  if (cancelled) out = patchObjectAtPath(out, "operations.workQueue.detail", { status: "CANCELLED" });
  return out;
};

// ── Dead letters ───────────────────────────────────────────────────────────
// Requeue / Acknowledge (single + batch) -> DeadLetters / DeadLetterDetail (status + note).

const DL_KEY = "deadLetterPatches";
// A single patch applied to EVERY awaiting row (requeueAll / acknowledgeAll), which carry no ids.
const DL_ALL_KEY = "deadLetterAllPatch";
type DeadLetterPatch = { status: string; resolutionNote?: string; resolvedAt: string };

function patchDeadLetters(store: MockStore, action: string, ids: number[], patch: DeadLetterPatch) {
  store.update(action, (draft) => {
    const current = { ...(draft[DL_KEY] as Record<string, DeadLetterPatch> | undefined) };
    for (const id of ids) current[String(id)] = patch;
    draft[DL_KEY] = current;
  });
}

const requeueDeadLetter: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  patchDeadLetters(store, "RequeueDeadLetter", [id], { status: "RETRIED", resolvedAt: nowIso() });
  return wrap("operations.deadLetters.requeueDeadLetter", {
    success: true,
    workQueueId: 9_000_000 + id,
    message: "Dead letter requeued.",
  });
};

const acknowledgeDeadLetter: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  const note = (variables.note as string) ?? "";
  patchDeadLetters(store, "AcknowledgeDeadLetter", [id], {
    status: "ACKNOWLEDGED",
    resolutionNote: note,
    resolvedAt: nowIso(),
  });
  return wrap("operations.deadLetters.acknowledgeDeadLetter", {
    success: true,
    message: "Dead letter acknowledged.",
  });
};

const requeueDeadLetters: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  patchDeadLetters(store, "RequeueDeadLetters", ids, { status: "RETRIED", resolvedAt: nowIso() });
  return wrap("operations.deadLetters.requeueDeadLetters", {
    count: ids.length,
    message: `${ids.length} dead letter(s) requeued.`,
  });
};

const acknowledgeDeadLetters: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  const note = (variables.note as string) ?? "";
  patchDeadLetters(store, "AcknowledgeDeadLetters", ids, {
    status: "ACKNOWLEDGED",
    resolutionNote: note,
    resolvedAt: nowIso(),
  });
  return wrap("operations.deadLetters.acknowledgeDeadLetters", {
    count: ids.length,
    message: `${ids.length} dead letter(s) acknowledged.`,
  });
};

const requeueAllDeadLetters: MutationOverlay = (_variables, store) => {
  store.update("RequeueAllDeadLetters", (draft) => {
    draft[DL_ALL_KEY] = { status: "RETRIED", resolvedAt: nowIso() };
  });
  // The mock answers with a job that has already finished. Count is a stand-in (the mock cannot
  // know the true unresolved total).
  return wrap("operations.deadLetters.requeueAllDeadLetters", {
    id: "00000000-0000-4000-8000-000000000001",
    status: "SUCCEEDED",
    started: true,
    awaitingAtStart: 3,
    startedAt: nowIso(),
    finishedAt: nowIso(),
    count: 3,
    message: "Requeued 3 dead letter(s).",
  });
};

const acknowledgeAllDeadLetters: MutationOverlay = (variables, store) => {
  const note = (variables.note as string) ?? "";
  store.update("AcknowledgeAllDeadLetters", (draft) => {
    draft[DL_ALL_KEY] = { status: "ACKNOWLEDGED", resolutionNote: note, resolvedAt: nowIso() };
  });
  return wrap("operations.deadLetters.acknowledgeAllDeadLetters", {
    count: 3,
    message: "All dead letters acknowledged.",
  });
};

function deadLetterPatchFor(store: MockStore, id: number): DeadLetterPatch | undefined {
  return readDelta<Record<string, DeadLetterPatch>>(store, DL_KEY, {})[String(id)];
}

const readDeadLetters: QueryOverlay = (data, store) => {
  const patches = readDelta<Record<string, DeadLetterPatch>>(store, DL_KEY, {});
  const allPatch = store.getState()[DL_ALL_KEY] as DeadLetterPatch | undefined;
  if (Object.keys(patches).length === 0 && !allPatch) return data;
  return mapItemsAtPath(data, "operations.deadLetters.deadLetters.items", (item) => {
    const r = asRecord(item);
    if (!r) return item;
    const idPatch = patches[String(r.id)];
    if (idPatch) return { ...r, ...idPatch };
    if (allPatch && r.status === "AWAITING_INTERVENTION") return { ...r, ...allPatch };
    return item;
  });
};

const readDeadLetterDetail: QueryOverlay = (data, store, variables) => {
  const patch = deadLetterPatchFor(store, variables.id as number);
  if (!patch) return data;
  return patchObjectAtPath(data, "operations.deadLetters.deadLetter", patch);
};

// ── Executions ─────────────────────────────────────────────────────────────
// CancelExecution -> ExecutionDetail (cancellationRequested); RequeueExecution ACK.

const EXEC_CANCEL_KEY = "executionsCancelled";

const cancelExecution: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  store.update("CancelExecution", (draft) => {
    const current = (draft[EXEC_CANCEL_KEY] as number[] | undefined) ?? [];
    draft[EXEC_CANCEL_KEY] = [...new Set([...current, id])];
  });
  return wrap("operations.cancelExecution", {
    success: true,
    count: 1,
    message: "Cancellation requested",
  });
};

const cancelExecutions: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  store.update("CancelExecutions", (draft) => {
    const current = (draft[EXEC_CANCEL_KEY] as number[] | undefined) ?? [];
    draft[EXEC_CANCEL_KEY] = [...new Set([...current, ...ids])];
  });
  return wrap("operations.cancelExecutions", {
    success: true,
    count: ids.length,
    message: `Cancellation requested for ${ids.length} execution(s).`,
  });
};

// A re-queue queues a fresh entry; its id is what the detail page navigates to.
const requeueExecution: MutationOverlay = (variables, store) => {
  const id = nextQueuedId(store);
  addQueued(store, "RequeueExecution", queuedEntry(id, `Re-queue of run ${variables.id}`, 0, null));
  return wrap("operations.requeueExecution", {
    success: true,
    message: variables.askAfresh ? "Execution re-queued; it asks afresh." : "Execution re-queued.",
    id,
  });
};

// A resume queues a fresh entry, as a re-queue does, and the API answers it with no message. A run
// is resumed once at a time: while its resume is still queued, another is refused in the API's
// words.
const RESUMES_KEY = "executionResumes";

const resumeExecution: MutationOverlay = (variables, store) => {
  const runId = variables.id as number;
  const queued = readDelta<Record<string, number>>(store, RESUMES_KEY, {})[String(runId)];
  const cancelled = new Set(readDelta<number[]>(store, CANCELLED_KEY, []));
  if (queued != null && !cancelled.has(queued))
    return wrap("operations.resumeExecution", {
      success: false,
      message: `A resume of execution ${runId} is already queued (WorkQueue ${queued}); a run is resumed once at a time. Nothing was queued.`,
      id: null,
    });
  const id = nextQueuedId(store);
  const at = variables.from ? ` at ${variables.from}` : "";
  addQueued(store, "ResumeExecution", queuedEntry(id, `Resume of run ${runId}${at}`, 0, null));
  store.update("ResumeExecution", (draft) => {
    draft[RESUMES_KEY] = { ...(draft[RESUMES_KEY] as Record<string, number> | undefined), [String(runId)]: id };
  });
  return wrap("operations.resumeExecution", { success: true, message: null, id });
};

const readExecutionDetail: QueryOverlay = (data, store, variables) => {
  const id = variables.id as number;
  const run = readDelta<Record<string, Rec>>(store, RUN_KEY, {})[String(id)];
  let out = data;
  if (run) out = patchObjectAtPath(out, "operations.executionDetail", { ...run, cancellationRequested: false, childCount: 0 });
  const cancelled = new Set(readDelta<number[]>(store, EXEC_CANCEL_KEY, []));
  if (cancelled.has(id))
    out = patchObjectAtPath(out, "operations.executionDetail", { cancellationRequested: true });
  return out;
};

// The list shows a cancel request too: on the rows cancelled by id, and, for a list scoped to a
// group whose running executions were cancelled (cancelGroups), on every active row.
const ACTIVE = new Set(["PENDING", "IN_PROGRESS"]);

const readExecutions: QueryOverlay = (data, store, variables) => {
  const cancelled = new Set(readDelta<number[]>(store, EXEC_CANCEL_KEY, []));
  const groups = new Set(readDelta<number[]>(store, GROUP_CANCEL_KEY, []));
  const groupCancelled = variables.manifestGroupId != null && groups.has(variables.manifestGroupId as number);
  if (cancelled.size === 0 && !groupCancelled) return data;
  return mapItemsAtPath(data, "operations.executions.items", (item) => {
    const r = asRecord(item);
    if (!r || !ACTIVE.has(String(r.trainState))) return item;
    return cancelled.has(r.id as number) || groupCancelled ? { ...r, cancellationRequested: true } : item;
  });
};

// ── Manifests ──────────────────────────────────────────────────────────────
// UpdateManifest (by id) / Enable / Disable (by externalId) / Trigger -> Manifests / ManifestDetail.

const MAN_BY_ID_KEY = "manifestPatchesById";
const MAN_BY_EXT_KEY = "manifestPatchesByExternalId";

function manifestPatchFromInput(input: Rec): Rec {
  const patch: Rec = {};
  if (input.isEnabled != null) patch.isEnabled = input.isEnabled;
  if (input.maxRetries != null) patch.maxRetries = input.maxRetries;
  if (input.priority != null) patch.priority = input.priority;
  if (input.scheduleType != null) patch.scheduleType = input.scheduleType;
  if (input.cronExpression != null) patch.cronExpression = input.cronExpression;
  if (input.intervalSeconds != null) patch.intervalSeconds = input.intervalSeconds;
  if (input.clearTimeout) patch.timeoutSeconds = null;
  else if (input.timeoutSeconds != null) patch.timeoutSeconds = input.timeoutSeconds;
  return patch;
}

const updateManifest: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  const patch = manifestPatchFromInput(asRecord(variables.input) ?? {});
  store.update("UpdateManifest", (draft) => {
    const current = { ...(draft[MAN_BY_ID_KEY] as Record<string, Rec> | undefined) };
    current[String(id)] = { ...current[String(id)], ...patch };
    draft[MAN_BY_ID_KEY] = current;
  });
  return wrap("operations.updateManifest", { success: true, message: "Manifest updated" });
};

function setManifestEnabled(store: MockStore, action: string, externalId: string, isEnabled: boolean) {
  store.update(action, (draft) => {
    const current = { ...(draft[MAN_BY_EXT_KEY] as Record<string, Rec> | undefined) };
    current[externalId] = { ...current[externalId], isEnabled };
    draft[MAN_BY_EXT_KEY] = current;
  });
}

const enableManifest: MutationOverlay = (variables, store) => {
  setManifestEnabled(store, "EnableManifest", variables.externalId as string, true);
  return wrap("operations.enableManifest", { success: true, message: "Manifest enabled" });
};

const disableManifest: MutationOverlay = (variables, store) => {
  setManifestEnabled(store, "DisableManifest", variables.externalId as string, false);
  return wrap("operations.disableManifest", { success: true, message: "Manifest disabled" });
};

const triggerManifest: MutationOverlay = (variables) =>
  wrap("operations.triggerManifest", {
    success: true,
    message: variables.askAfresh ? "Manifest triggered; a released retry asks afresh" : "Manifest triggered",
  });

function patchManifestsById(store: MockStore, action: string, ids: number[], patch: Rec) {
  store.update(action, (draft) => {
    const current = { ...(draft[MAN_BY_ID_KEY] as Record<string, Rec> | undefined) };
    for (const id of ids) current[String(id)] = { ...current[String(id)], ...patch };
    draft[MAN_BY_ID_KEY] = current;
  });
}

const setManifestsEnabled: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  const enabled = Boolean(variables.enabled);
  patchManifestsById(store, "SetManifestsEnabled", ids, { isEnabled: enabled });
  return wrap("operations.setManifestsEnabled", {
    success: true,
    count: ids.length,
    message: `${ids.length} manifest(s) ${enabled ? "enabled" : "disabled"}.`,
  });
};

const setManifestsReplayDecisionsOnRetry: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  const replay = Boolean(variables.replay);
  patchManifestsById(store, "SetManifestsReplayDecisionsOnRetry", ids, { replayDecisionsOnRetry: replay });
  return wrap("operations.setManifestsReplayDecisionsOnRetry", {
    success: true,
    count: ids.length,
    message: replay
      ? `Retries of ${ids.length} manifest(s) replay decisions.`
      : `Retries of ${ids.length} manifest(s) ask afresh.`,
  });
};

function manifestPatch(store: MockStore, item: Rec): Rec | undefined {
  const byId = readDelta<Record<string, Rec>>(store, MAN_BY_ID_KEY, {})[String(item.id)];
  const byExt = readDelta<Record<string, Rec>>(store, MAN_BY_EXT_KEY, {})[String(item.externalId)];
  if (!byId && !byExt) return undefined;
  return { ...byId, ...byExt };
}

const readManifests: QueryOverlay = (data, store) => {
  const byId = readDelta<Record<string, Rec>>(store, MAN_BY_ID_KEY, {});
  const byExt = readDelta<Record<string, Rec>>(store, MAN_BY_EXT_KEY, {});
  if (Object.keys(byId).length === 0 && Object.keys(byExt).length === 0) return data;
  return mapItemsAtPath(data, "operations.manifests.items", (item) => {
    const r = asRecord(item);
    const patch = r ? manifestPatch(store, r) : undefined;
    return r && patch ? { ...r, ...patch } : item;
  });
};

const readManifestDetail: QueryOverlay = (data, store) => {
  const root = asRecord(data);
  const manifest = asRecord(root?.operations && asRecord(root.operations)?.manifestDetail);
  if (!manifest) return data;
  const patch = manifestPatch(store, manifest);
  if (!patch) return data;
  return patchObjectAtPath(data, "operations.manifestDetail", patch);
};

// ── Manifest groups ────────────────────────────────────────────────────────
// UpdateManifestGroup -> ManifestGroups / ManifestGroupDetail; Trigger / Cancel ACK.

const GROUP_KEY = "groupPatches";

function groupPatchFromInput(input: Rec): Rec {
  const patch: Rec = {};
  if (input.clearMaxActiveJobs) patch.maxActiveJobs = null;
  else if (input.maxActiveJobs != null) patch.maxActiveJobs = input.maxActiveJobs;
  if (input.priority != null) patch.priority = input.priority;
  if (input.isEnabled != null) patch.isEnabled = input.isEnabled;
  return patch;
}

const updateManifestGroup: MutationOverlay = (variables, store) => {
  const id = variables.id as number;
  const patch = groupPatchFromInput(asRecord(variables.input) ?? {});
  store.update("UpdateManifestGroup", (draft) => {
    const current = { ...(draft[GROUP_KEY] as Record<string, Rec> | undefined) };
    current[String(id)] = { ...current[String(id)], ...patch };
    draft[GROUP_KEY] = current;
  });
  return wrap("operations.manifestGroups.updateManifestGroup", {
    success: true,
    message: "Group updated",
  });
};

const triggerGroup: MutationOverlay = () =>
  wrap("operations.triggerGroup", { success: true, count: 0, message: "Group triggered" });

const cancelGroup: MutationOverlay = () =>
  wrap("operations.cancelGroup", { success: true, count: 0, message: "Group cancellation requested" });

// ── Batch triggers ─────────────────────────────────────────────────────────
// TriggerManifests / TriggerGroups -> WorkQueue (a queued entry per triggered manifest) and the
// next trigger of the same ids (counted as already queued). CancelGroups -> Executions scoped to
// the group. Each refuses an empty list or more than 1000 ids, as the API does. The mock cannot
// know which ids exist, so any id at or above DELETED_ID_FLOOR stands for a manifest or group
// deleted since it was listed: it is skipped with a note. A group holds MOCK_GROUP_SIZE manifests.

export const DELETED_ID_FLOOR = 1_000_000;
const MAX_BATCH = 1000;
const MOCK_GROUP_SIZE = 2;
const TRIGGERED_GROUPS_KEY = "triggeredGroupIds";
const GROUP_CANCEL_KEY = "cancelledGroupIds";

function batchRefusal(ids: number[]): string | null {
  if (ids.length === 0) return "No ids were given.";
  if (ids.length > MAX_BATCH) return `At most ${MAX_BATCH} ids can be sent at once.`;
  return null;
}

function emptyBatch(message: string): Rec {
  return { success: false, matched: 0, queued: 0, alreadyQueued: 0, tooLateToAskAfresh: 0, skipped: 0, message, notes: [] };
}

function batchMessage(queued: number, alreadyQueued: number, skipped: number, matched: number, of: number, noun: string, askAfresh: boolean): string {
  const parts = [`${queued} queued`];
  if (alreadyQueued) parts.push(`${alreadyQueued} already queued (that entry now runs as the trigger)`);
  if (skipped) parts.push(`${skipped} not found`);
  return `${parts.join(", ")} across ${matched} of ${of} ${noun}(s)${askAfresh ? ", asking afresh" : ""}.`;
}

const triggerManifests: MutationOverlay = (variables, store) => {
  const ids = [...new Set((variables.ids as number[] | undefined) ?? [])];
  const askAfresh = Boolean(variables.askAfresh);
  const path = "operations.triggerManifests";
  const refusal = batchRefusal(ids);
  if (refusal) return wrap(path, emptyBatch(refusal));
  const queuedByManifest = new Set(
    Object.values(readDelta<Record<string, Rec>>(store, QUEUED_KEY, {}))
      .filter((e) => e.status === "QUEUED" && e.manifestId != null)
      .map((e) => e.manifestId as number),
  );
  const notes: Rec[] = [];
  let queued = 0;
  let alreadyQueued = 0;
  for (const id of ids) {
    if (id >= DELETED_ID_FLOOR) notes.push({ id, message: `Manifest ${id} not found.` });
    else if (queuedByManifest.has(id)) alreadyQueued++;
    else {
      const entry = queuedEntry(nextQueuedId(store), `Trax.Mock.Manifests.Manifest${id}`, 0, null);
      addQueued(store, "TriggerManifests", { ...entry, manifestId: id });
      queued++;
    }
  }
  const matched = ids.length - notes.length;
  return wrap(path, {
    success: true,
    matched,
    queued,
    alreadyQueued,
    tooLateToAskAfresh: 0,
    skipped: notes.length,
    message: batchMessage(queued, alreadyQueued, notes.length, matched, ids.length, "manifest", askAfresh),
    notes,
  });
};

const triggerGroups: MutationOverlay = (variables, store) => {
  const ids = [...new Set((variables.ids as number[] | undefined) ?? [])];
  const path = "operations.triggerGroups";
  const refusal = batchRefusal(ids);
  if (refusal) return wrap(path, emptyBatch(refusal));
  const before = new Set(readDelta<number[]>(store, TRIGGERED_GROUPS_KEY, []));
  const notes = ids.filter((id) => id >= DELETED_ID_FLOOR).map((id) => ({ id, message: `Manifest group ${id} not found.` }));
  const found = ids.filter((id) => id < DELETED_ID_FLOOR);
  const queued = found.filter((id) => !before.has(id)).length * MOCK_GROUP_SIZE;
  const alreadyQueued = found.filter((id) => before.has(id)).length * MOCK_GROUP_SIZE;
  store.update("TriggerGroups", (draft) => {
    draft[TRIGGERED_GROUPS_KEY] = [...new Set([...before, ...found])];
  });
  return wrap(path, {
    success: true,
    matched: found.length,
    queued,
    alreadyQueued,
    tooLateToAskAfresh: 0,
    skipped: notes.length,
    message: batchMessage(queued, alreadyQueued, notes.length, found.length, ids.length, "manifest group", false),
    notes,
  });
};

const cancelGroups: MutationOverlay = (variables, store) => {
  const ids = [...new Set((variables.ids as number[] | undefined) ?? [])];
  const path = "operations.cancelGroups";
  const refusal = batchRefusal(ids);
  if (refusal) return wrap(path, { success: false, count: 0, message: refusal });
  const found = ids.filter((id) => id < DELETED_ID_FLOOR);
  store.update("CancelGroups", (draft) => {
    draft[GROUP_CANCEL_KEY] = [...new Set([...((draft[GROUP_CANCEL_KEY] as number[] | undefined) ?? []), ...found])];
  });
  // The mock does not know how many runs a group has running; it counts one per group found.
  return wrap(path, {
    success: true,
    count: found.length,
    message: `Cancellation requested for ${found.length} execution(s) across ${found.length} of ${ids.length} manifest group(s).`,
  });
};

// setAllManifestGroupsEnabled applies to every group, including ones the session never listed.
const GROUP_ALL_KEY = "groupAllPatch";

const setManifestGroupsEnabled: MutationOverlay = (variables, store) => {
  const ids = (variables.ids as number[] | undefined) ?? [];
  const enabled = Boolean(variables.enabled);
  store.update("SetManifestGroupsEnabled", (draft) => {
    const current = { ...(draft[GROUP_KEY] as Record<string, Rec> | undefined) };
    for (const id of ids) current[String(id)] = { ...current[String(id)], isEnabled: enabled };
    draft[GROUP_KEY] = current;
  });
  return wrap("operations.manifestGroups.setManifestGroupsEnabled", {
    success: true,
    count: ids.length,
    message: `${ids.length} group(s) ${enabled ? "enabled" : "disabled"}.`,
  });
};

const setAllManifestGroupsEnabled: MutationOverlay = (variables, store) => {
  const enabled = Boolean(variables.enabled);
  store.update("SetAllManifestGroupsEnabled", (draft) => {
    // A later "all" overrides any earlier per-group flag.
    const current = { ...(draft[GROUP_KEY] as Record<string, Rec> | undefined) };
    for (const key of Object.keys(current)) {
      const rest = { ...current[key] };
      delete rest.isEnabled;
      current[key] = rest;
    }
    draft[GROUP_KEY] = current;
    draft[GROUP_ALL_KEY] = { isEnabled: enabled };
  });
  return wrap("operations.manifestGroups.setAllManifestGroupsEnabled", {
    success: true,
    count: null,
    message: `All groups ${enabled ? "enabled" : "disabled"}.`,
  });
};

function groupPatch(store: MockStore, id: unknown): Rec | undefined {
  const all = readDelta<Rec | undefined>(store, GROUP_ALL_KEY, undefined);
  const byId = readDelta<Record<string, Rec>>(store, GROUP_KEY, {})[String(id)];
  if (!all && !byId) return undefined;
  return { ...all, ...byId };
}

const readManifestGroups: QueryOverlay = (data, store) => {
  return mapItemsAtPath(data, "operations.manifestGroups.groups.items", (item) => {
    const r = asRecord(item);
    const patch = r ? groupPatch(store, r.id) : undefined;
    return r && patch ? { ...r, ...patch } : item;
  });
};

const readManifestGroupDetail: QueryOverlay = (data, store, variables) => {
  const patch = groupPatch(store, variables.id);
  if (!patch) return data;
  return patchObjectAtPath(data, "operations.manifestGroups.group", patch);
};

// ── Scheduler config ───────────────────────────────────────────────────────
// UpdateScheduler -> SchedulerConfig (operations.config.scheduler).

const SCHED_KEY = "schedulerPatch";

function schedulerPatchFromInput(input: Rec): Rec {
  const patch: Rec = { ...input };
  delete patch.clearMaxActiveJobs;
  if (input.clearMaxActiveJobs) patch.maxActiveJobs = null;
  return patch;
}

const updateScheduler: MutationOverlay = (variables, store) => {
  const patch = schedulerPatchFromInput(asRecord(variables.input) ?? {});
  store.update("UpdateScheduler", (draft) => {
    draft[SCHED_KEY] = { ...(draft[SCHED_KEY] as Rec | undefined), ...patch };
  });
  return wrap("operations.config.updateScheduler", { success: true, message: "Scheduler updated" });
};

const readSchedulerConfig: QueryOverlay = (data, store) => {
  const patch = readDelta<Rec>(store, SCHED_KEY, {});
  if (Object.keys(patch).length === 0) return data;
  return patchObjectAtPath(data, "operations.config.scheduler", patch);
};

// ── Effects ────────────────────────────────────────────────────────────────
// SetEffectEnabled -> Effects (enabled, by fullName).

const EFFECT_KEY = "effectEnabled";

const setEffectEnabled: MutationOverlay = (variables, store) => {
  const fullName = String(variables.fullName);
  const enabled = Boolean(variables.enabled);
  store.update("SetEffectEnabled", (draft) => {
    draft[EFFECT_KEY] = { ...(draft[EFFECT_KEY] as Record<string, boolean> | undefined), [fullName]: enabled };
  });
  return wrap("operations.setEffectEnabled", {
    success: true,
    message: `${fullName.split(".").pop()} ${enabled ? "enabled" : "disabled"}.`,
  });
};

// ConfigureEffect -> Effects (each field's value and hasValue, and the configuration JSON). The
// mock checks values the way the API does, against the settings the session last read for that
// effect (a store-scoped memo, not session state), all or none.
const EFFECT_SETTINGS_KEY = "effectSettings";
const effectFieldsSeen = new WeakMap<MockStore, Map<string, { typeName: string | null; fields: Rec[] }>>();

function rememberEffectFields(store: MockStore, data: unknown) {
  const effects = asRecord(asRecord(data)?.operations)?.effects;
  if (!Array.isArray(effects)) return;
  const seen = effectFieldsSeen.get(store) ?? new Map();
  for (const e of effects) {
    const r = asRecord(e);
    if (r && r.isConfigurable && Array.isArray(r.fields))
      seen.set(String(r.fullName), { typeName: (r.configurationTypeName as string | null) ?? null, fields: r.fields as Rec[] });
  }
  effectFieldsSeen.set(store, seen);
}

function checkSettingValue(field: Rec, value: string | null): string | null {
  const blank = value == null || value.trim() === "";
  if (field.kind === "SET_IN_CODE") return "This setting is set in code and cannot be changed here.";
  if (blank) return field.nullable || field.typeName === "String" ? null : "A value is required.";
  const v = value!.trim();
  if (field.kind === "BOOLEAN") return /^(true|false)$/i.test(v) ? null : `'${v}' is not true or false.`;
  if (field.kind === "ENUM") {
    const members = (field.enumValues as string[] | null) ?? [];
    return members.includes(v) ? null : `'${v}' is not one of ${members.join(", ")}.`;
  }
  if (/^(U?Int(16|32|64)|S?Byte)$/.test(String(field.typeName)))
    return /^-?\d+$/.test(v) ? null : `'${v}' is not a whole number that fits a ${field.typeName}, with no thousands separators.`;
  if (/^(Double|Single|Decimal)$/.test(String(field.typeName)))
    return /^-?\d+(\.\d+)?$/.test(v) ? null : `'${v}' is not a number, with . for the decimal point.`;
  return null;
}

const configureEffect: MutationOverlay = (variables, store) => {
  const fullName = String(variables.fullName ?? "");
  const values = ((variables.values as Rec[] | undefined) ?? []).map((v) => ({
    name: String(v.name),
    value: (v.value as string | null | undefined) ?? null,
  }));
  const path = "operations.configureEffect";
  const refuse = (message: string, errors: Rec[] = []) => wrap(path, { success: false, count: 0, message, errors });
  const effect = effectFieldsSeen.get(store)?.get(fullName);
  if (!effect) return refuse(`No effect named '${fullName}' is registered in this process.`);
  if (values.length === 0) return refuse("No settings were given.");
  const errors: Rec[] = [];
  const named = new Set<string>();
  for (const { name, value } of values) {
    const field = effect.fields.find((f) => f.name === name);
    const problem = named.has(name)
      ? "Given more than once."
      : !field
        ? "No such setting."
        : checkSettingValue(field, value);
    named.add(name);
    if (problem) errors.push({ field: name, message: problem });
  }
  if (errors.length > 0)
    return refuse(`The configuration was not saved: ${errors.map((e) => `${e.field}: ${e.message}`).join(" ")}`, errors);
  store.update("ConfigureEffect", (draft) => {
    const all = { ...(draft[EFFECT_SETTINGS_KEY] as Record<string, Record<string, string | null>> | undefined) };
    all[fullName] = { ...all[fullName], ...Object.fromEntries(values.map((v) => [v.name, v.value?.trim() ? v.value.trim() : null])) };
    draft[EFFECT_SETTINGS_KEY] = all;
  });
  const typeName = effect.typeName?.split(".").pop() ?? fullName.split(".").pop();
  return wrap(path, {
    success: true,
    count: values.length,
    message: `${typeName} updated in this process. Changes apply to the next train execution.`,
    errors: [],
  });
};

const camel = (name: string) => name.charAt(0).toLowerCase() + name.slice(1);

function applyEffectSettings(effect: Rec, written: Record<string, string | null>): Rec {
  const fields = Array.isArray(effect.fields)
    ? (effect.fields as Rec[]).map((f) => {
        if (!(String(f.name) in written)) return f;
        const value = written[String(f.name)];
        return f.sensitive ? { ...f, hasValue: value != null } : { ...f, value, hasValue: value != null };
      })
    : effect.fields;
  let configuration = effect.configuration;
  if (typeof configuration === "string") {
    try {
      const json = JSON.parse(configuration) as Rec;
      for (const f of (effect.fields as Rec[] | undefined) ?? [])
        if (!f.sensitive && String(f.name) in written) json[camel(String(f.name))] = written[String(f.name)];
      configuration = JSON.stringify(json);
    } catch {
      /* not JSON: leave it */
    }
  }
  return { ...effect, fields, configuration };
}

const readEffects: QueryOverlay = (data, store) => {
  rememberEffectFields(store, data);
  const enabled = readDelta<Record<string, boolean>>(store, EFFECT_KEY, {});
  const settings = readDelta<Record<string, Record<string, string | null>>>(store, EFFECT_SETTINGS_KEY, {});
  if (Object.keys(enabled).length === 0 && Object.keys(settings).length === 0) return data;
  return mapItemsAtPath(data, "operations.effects", (item) => {
    let r = asRecord(item);
    if (!r) return item;
    if (String(r.fullName) in enabled) r = { ...r, enabled: enabled[String(r.fullName)] };
    const written = settings[String(r.fullName)];
    return written ? applyEffectSettings(r, written) : r;
  });
};

// ── Log levels ─────────────────────────────────────────────────────────────
// SetLogLevels -> LogLevels (level, overridden). A category must be one the session read (the
// configured ones), matched ignoring case, or the whole list is refused. The mock host sets
// Microsoft.Hosting.Lifetime itself after Trax, so a level saved for it is reported as not applied.
const LOG_LEVELS_KEY = "logLevelOverrides";
const logCategoriesSeen = new WeakMap<MockStore, Map<string, string>>();
const HOST_SET_CATEGORIES = new Set(["microsoft.hosting.lifetime"]);

function pascalLevel(level: string): string {
  return level.charAt(0) + level.slice(1).toLowerCase();
}

const setLogLevels: MutationOverlay = (variables, store) => {
  const levels = ((variables.levels as Rec[] | undefined) ?? []).map((l) => ({
    category: String(l.category),
    level: pascalLevel(String(l.level)),
  }));
  const path = "operations.config.setLogLevels";
  const refuse = (message: string) => wrap(path, { success: false, count: 0, notApplied: [], message });
  if (levels.length === 0) return refuse("No levels were given.");
  const known = logCategoriesSeen.get(store) ?? new Map<string, string>();
  const unknown = levels.filter((l) => !known.has(l.category.toLowerCase())).map((l) => l.category);
  if (unknown.length > 0) return refuse(`Not a category configured under Logging:LogLevel: ${unknown.join(", ")}.`);
  const byCategory = new Map(levels.map((l) => [known.get(l.category.toLowerCase())!, l.level]));
  store.update("SetLogLevels", (draft) => {
    draft[LOG_LEVELS_KEY] = { ...(draft[LOG_LEVELS_KEY] as Record<string, string> | undefined), ...Object.fromEntries(byCategory) };
  });
  return wrap(path, {
    success: true,
    count: byCategory.size,
    notApplied: [...byCategory.keys()].filter((c) => HOST_SET_CATEGORIES.has(c.toLowerCase())),
    message: `${byCategory.size} log level(s) set in this process.`,
  });
};

const readLogLevels: QueryOverlay = (data, store) => {
  const list = asRecord(asRecord(asRecord(data)?.operations)?.config)?.logLevels;
  if (Array.isArray(list)) {
    const seen = new Map<string, string>();
    for (const l of list) {
      const r = asRecord(l);
      if (r) seen.set(String(r.category).toLowerCase(), String(r.category));
    }
    logCategoriesSeen.set(store, seen);
  }
  const overrides = readDelta<Record<string, string>>(store, LOG_LEVELS_KEY, {});
  if (Object.keys(overrides).length === 0) return data;
  return mapItemsAtPath(data, "operations.config.logLevels", (item) => {
    const r = asRecord(item);
    if (!r || !(String(r.category) in overrides)) return item;
    // A category the host sets itself keeps filtering at its own level.
    if (HOST_SET_CATEGORIES.has(String(r.category).toLowerCase())) return { ...r, overridden: true };
    return { ...r, level: overrides[String(r.category)], overridden: true };
  });
};

// ── Persisted operations ───────────────────────────────────────────────────
// Upload / Deactivate / Restore -> PersistedOperations (list) / PersistedOperationDetail (row +
// history). The mock stands in for the server's guardrails just far enough to exercise the UI:
// unbalanced braces are a PARSE_FAILED, and re-uploading an id this session uploaded with a
// different selection set is a SHAPE_DIFF_VIOLATION unless bypassShapeDiff is set.

const PO_KEY = "persistedOperations";
const PO_PATCH_KEY = "persistedOperationPatches";
const PO_HISTORY_KEY = "persistedOperationHistory";

const poKey = (id: unknown, tenantKey: unknown) => `${(tenantKey as string | null) ?? ""}|${String(id)}`;

// A stand-in for the server's shape fingerprint: a hash of the selection set (everything after the
// operation header), so renaming the operation keeps it and changing a field does not.
function mockFingerprint(document: string): string {
  const body = document.slice(Math.max(0, document.indexOf("{"))).replace(/\s+/g, " ").trim();
  let h = 0x811c9dc5;
  for (let i = 0; i < body.length; i++) h = Math.imul(h ^ body.charCodeAt(i), 0x01000193) >>> 0;
  return h.toString(16).padStart(8, "0").repeat(8);
}

function poError(code: string, message: string, extra: Rec = {}): Rec {
  return { code, message, locations: null, path: null, oldFingerprint: null, newFingerprint: null, ...extra };
}

function poPayload(path: string, operation: Rec | null, errors: Rec[]): Rec {
  return wrap(`operations.persistedOperations.${path}`, {
    success: operation != null && errors.length === 0,
    operation,
    errors,
  });
}

function addPoHistory(draft: Record<string, unknown>, key: string, entry: Rec) {
  const history = { ...(draft[PO_HISTORY_KEY] as Record<string, Rec[]> | undefined) };
  history[key] = [{ historyId: takeId(draft, "persistedOperationHistory", 1_000_001), changedAt: nowIso(), ...entry }, ...(history[key] ?? [])];
  draft[PO_HISTORY_KEY] = history;
}

const uploadPersistedOperation: MutationOverlay = (variables, store) => {
  const input = asRecord(variables.input) ?? {};
  const id = String(input.id ?? "").trim();
  const document = String(input.document ?? "");
  const tenantKey = (input.tenantKey as string | null | undefined) || null;
  const path = "uploadPersistedOperation";
  if (!id) return poPayload(path, null, [poError("INVALID_INPUT", "id is required.")]);
  if (!document.trim()) return poPayload(path, null, [poError("INVALID_INPUT", "document is required.")]);

  const opens = (document.match(/{/g) ?? []).length;
  const closes = (document.match(/}/g) ?? []).length;
  if (opens !== closes) {
    const lines = document.split("\n");
    return poPayload(path, null, [
      poError("PARSE_FAILED", "Expected a `RightBrace`-token, but found a `EndOfFile`-token.", {
        locations: [{ line: lines.length, column: lines[lines.length - 1].length + 1 }],
      }),
    ]);
  }

  const key = poKey(id, tenantKey);
  const existing = readDelta<Record<string, Rec>>(store, PO_KEY, {})[key];
  const fingerprint = mockFingerprint(document);
  if (existing && existing.shapeFingerprint !== fingerprint && !input.bypassShapeDiff) {
    const old = String(existing.shapeFingerprint);
    return poPayload(path, null, [
      poError(
        "SHAPE_DIFF_VIOLATION",
        `Persisted operation '${id}' edit rejected: response shape changed (old fingerprint ${old.slice(0, 8)}…, new ${fingerprint.slice(0, 8)}…). Pass UpsertOptions { BypassShapeDiff = true } if the change is shape-safe.`,
        { oldFingerprint: old, newFingerprint: fingerprint },
      ),
    ]);
  }

  const operation: Rec = {
    id,
    tenantKey,
    operationName: /(?:query|mutation|subscription)\s+(\w+)/.exec(document)?.[1] ?? "",
    version: Number(input.version ?? 0),
    document,
    shapeFingerprint: fingerprint,
    isActive: true,
    deprecationReason: null,
    description: (input.description as string | null | undefined) ?? null,
    createdAt: (existing?.createdAt as string | undefined) ?? nowIso(),
    updatedAt: nowIso(),
  };
  store.update("UploadPersistedOperation", (draft) => {
    draft[PO_KEY] = { ...(draft[PO_KEY] as Record<string, Rec> | undefined), [key]: operation };
    const patches = { ...(draft[PO_PATCH_KEY] as Record<string, Rec> | undefined) };
    delete patches[key];
    draft[PO_PATCH_KEY] = patches;
    addPoHistory(draft, key, {
      id,
      tenantKey,
      document,
      shapeFingerprint: fingerprint,
      changeType: "Upsert",
      changedReason: input.bypassShapeDiff ? "shape-diff bypassed" : null,
    });
  });
  return poPayload(path, operation, []);
};

function setPersistedOperationActive(
  store: MockStore,
  action: string,
  path: string,
  input: Rec,
  isActive: boolean,
): Record<string, unknown> {
  const id = String(input.id ?? "");
  const tenantKey = (input.tenantKey as string | null | undefined) || null;
  const reason = isActive ? null : String(input.reason ?? "").trim();
  if (!isActive && !reason) return poPayload(path, null, [poError("INVALID_INPUT", "reason is required.")]);
  const key = poKey(id, tenantKey);
  const patch: Rec = { isActive, deprecationReason: reason, updatedAt: nowIso() };
  let operation: Rec = { id, tenantKey, version: 0, shapeFingerprint: "", ...patch };
  store.update(action, (draft) => {
    const ops = { ...(draft[PO_KEY] as Record<string, Rec> | undefined) };
    if (ops[key]) {
      ops[key] = { ...ops[key], ...patch };
      operation = ops[key];
      draft[PO_KEY] = ops;
    } else {
      draft[PO_PATCH_KEY] = { ...(draft[PO_PATCH_KEY] as Record<string, Rec> | undefined), [key]: patch };
    }
    addPoHistory(draft, key, {
      id,
      tenantKey,
      document: (ops[key]?.document as string | undefined) ?? "",
      shapeFingerprint: (ops[key]?.shapeFingerprint as string | undefined) ?? mockFingerprint(id),
      changeType: isActive ? "Restore" : "Deactivate",
      changedReason: reason,
    });
  });
  return poPayload(path, operation, []);
}

const deactivatePersistedOperation: MutationOverlay = (variables, store) =>
  setPersistedOperationActive(store, "DeactivatePersistedOperation", "deactivatePersistedOperation", asRecord(variables.input) ?? {}, false);

const restorePersistedOperation: MutationOverlay = (variables, store) =>
  setPersistedOperationActive(store, "RestorePersistedOperation", "restorePersistedOperation", asRecord(variables.input) ?? {}, true);

function poMatches(op: Rec, filter: Rec | undefined): boolean {
  if (!filter) return true;
  if (filter.isActive != null && op.isActive !== filter.isActive) return false;
  if (filter.tenantKey != null && ((op.tenantKey as string | null) ?? "") !== filter.tenantKey) return false;
  if (filter.idStartsWith && !String(op.id).startsWith(String(filter.idStartsWith))) return false;
  return true;
}

// A row deactivated or restored without having been uploaded this session is patched where the
// base list returns it; a status filter applied by the base before the patch can still leave it
// out (the mock does not hold its full row to add back).
const readPersistedOperations: QueryOverlay = (data, store, variables) => {
  const ops = readDelta<Record<string, Rec>>(store, PO_KEY, {});
  const patches = readDelta<Record<string, Rec>>(store, PO_PATCH_KEY, {});
  if (Object.keys(ops).length === 0 && Object.keys(patches).length === 0) return data;
  const filter = asRecord(variables.filter);
  const base = "operations.persistedOperations.persistedOperations";
  let added = 0;
  const out = updateItemsAtPath(data, `${base}.items`, (items) => {
    const seen = new Set<string>();
    const merged = items
      .map((item) => {
        const r = asRecord(item);
        if (!r) return item;
        const key = poKey(r.id, r.tenantKey);
        seen.add(key);
        return ops[key] ? { ...r, ...ops[key] } : patches[key] ? { ...r, ...patches[key] } : r;
      })
      .filter((item) => poMatches(asRecord(item) ?? {}, filter));
    const fresh = Number(variables.skip ?? 0) > 0
      ? []
      : Object.entries(ops)
          .filter(([key, op]) => !seen.has(key) && poMatches(op, filter))
          .map(([, op]) => op);
    added = fresh.length - (items.length - merged.length);
    return [...fresh, ...merged];
  });
  const root = asRecord(asRecord(asRecord(asRecord(out)?.operations)?.persistedOperations)?.persistedOperations);
  return root ? patchObjectAtPath(out, base, { totalCount: Number(root.totalCount ?? 0) + added }) : out;
};

const readPersistedOperationDetail: QueryOverlay = (data, store, variables) => {
  const key = poKey(variables.id, variables.tenantKey);
  const stored = readDelta<Record<string, Rec>>(store, PO_KEY, {})[key];
  const patch = readDelta<Record<string, Rec>>(store, PO_PATCH_KEY, {})[key];
  const history = readDelta<Record<string, Rec[]>>(store, PO_HISTORY_KEY, {})[key] ?? [];
  if (!stored && !patch && history.length === 0) return data;
  const ns = asRecord(asRecord(asRecord(data)?.operations)?.persistedOperations) ?? {};
  const row = stored ?? (asRecord(ns.persistedOperation) && patch ? { ...asRecord(ns.persistedOperation), ...patch } : ns.persistedOperation);
  const baseHistory = Array.isArray(ns.persistedOperationHistory) ? ns.persistedOperationHistory : [];
  return wrap("operations.persistedOperations", {
    ...ns,
    persistedOperation: row ?? null,
    persistedOperationHistory: [...history, ...baseHistory],
  });
};

// ── State machines ─────────────────────────────────────────────────────────
// CancelMachineInstance -> MachineInstance (the run its state waits on) / WorkQueue / ExecutionDetail.
// The answer depends on the instance, which the mutation's variables do not carry, so the cancel
// reads it as the API looks it up, whatever a page read before. The messages are the API's.

const MACHINE_CANCEL_KEY = "machineInstancesCancelled";
const instanceKey = (machine: unknown, id: unknown) => `${machine}|${id}`;

export const USER_OWNED_CANCEL_REFUSAL =
  "Operators can cancel only a system-owned instance. A user-owned instance is read-only to operators: " +
  "its run is cancelled when its user leaves the state through one of the machine's own transitions.";

const cancelMachineInstance: MutationOverlay = async (variables, store, context) => {
  const { machine, id } = variables;
  const answer = (outcome: string, success: boolean, message: string) =>
    wrap("operations.cancelMachineInstance", { success, outcome, message, state: null });
  if (variables.ownerKind !== "SYSTEM") return answer("USER_OWNED", false, USER_OWNED_CANCEL_REFUSAL);
  const read = await context.query(MACHINE_INSTANCE, { machine, ownerKind: "SYSTEM", id, rowId: null });
  const instance = asRecord(asRecord(asRecord(read)?.operations)?.machineInstance);
  if (!instance) return answer("NOT_FOUND", false, `No system-owned instance of '${machine}' has id ${id}.`);
  if (!instance.hasLiveInvokedRun)
    return answer(
      "NO_LIVE_RUN",
      false,
      `Instance ${id} of '${machine}' is in '${instance.state}', which waits on no train run: there is nothing to cancel.`,
    );
  const queuedEntryId = (instance.queuedInvokedRunEntryId as number | null | undefined) ?? null;
  const liveRunId = ((instance.invokedRuns as Rec[] | undefined) ?? []).find((r) => r.isLive)?.id as number | undefined;
  store.update("CancelMachineInstance", (draft) => {
    const current = (draft[MACHINE_CANCEL_KEY] as string[] | undefined) ?? [];
    draft[MACHINE_CANCEL_KEY] = [...new Set([...current, instanceKey(machine, id)])];
  });
  // A queued run is cancelled before it starts. This mock registers no machine, so the instance
  // moves when a host that does applies the outcome.
  if (queuedEntryId != null) {
    addCancelled(store, [queuedEntryId]);
    return answer(
      "RUN_CANCELLED",
      true,
      `The queued run of instance ${id} of '${machine}' is cancelled and will not start. The instance moves ` +
        "through its state's OnCancelled edge when a host that registers the machine applies the outcome.",
    );
  }
  // A dispatched run has its cancel requested and stops at its next junction.
  if (liveRunId != null)
    store.update("CancelMachineInstance", (draft) => {
      const current = (draft[EXEC_CANCEL_KEY] as number[] | undefined) ?? [];
      draft[EXEC_CANCEL_KEY] = [...new Set([...current, liveRunId])];
    });
  return answer(
    "CANCEL_REQUESTED",
    true,
    `Cancellation requested for the run instance ${id} of '${machine}' waits on in '${instance.state}'. ` +
      "The run stops at its next junction, and the instance then moves through the state's OnCancelled edge.",
  );
};

const readMachineInstance: QueryOverlay = (data, store) => {
  const instance = asRecord(asRecord(asRecord(data)?.operations)?.machineInstance);
  if (!instance) return data;
  if (!readDelta<string[]>(store, MACHINE_CANCEL_KEY, []).includes(instanceKey(instance.machine, instance.id))) return data;
  const runs = (instance.invokedRuns as Rec[] | undefined) ?? [];
  // Cancelled: a queued run is no longer queued; a dispatched one has its cancel requested.
  return patchObjectAtPath(data, "operations.machineInstance", {
    queuedInvokedRunEntryId: null,
    invokedRuns: runs.map((r) => (r.isLive ? { ...r, cancellationRequested: true } : r)),
  });
};

// ── Registration ───────────────────────────────────────────────────────────

export const workQueueOverlay: StatefulOverlay = {
  mutations: {
    CancelWorkQueueEntries: cancelWorkQueueEntries,
    CancelWorkQueueEntry: cancelWorkQueueEntry,
    QueueTrain: queueTrain,
    RunTrain: runTrain,
  },
  queries: { WorkQueue: readWorkQueue, WorkQueueDetail: readWorkQueueDetail },
};

export const deadLetterOverlay: StatefulOverlay = {
  mutations: {
    RequeueDeadLetter: requeueDeadLetter,
    AcknowledgeDeadLetter: acknowledgeDeadLetter,
    RequeueDeadLetters: requeueDeadLetters,
    AcknowledgeDeadLetters: acknowledgeDeadLetters,
    RequeueAllDeadLetters: requeueAllDeadLetters,
    AcknowledgeAllDeadLetters: acknowledgeAllDeadLetters,
  },
  queries: { DeadLetters: readDeadLetters, DeadLetterDetail: readDeadLetterDetail },
};

export const executionOverlay: StatefulOverlay = {
  mutations: {
    CancelExecution: cancelExecution,
    CancelExecutions: cancelExecutions,
    RequeueExecution: requeueExecution,
    ResumeExecution: resumeExecution,
  },
  queries: { ExecutionDetail: readExecutionDetail, Executions: readExecutions },
};

export const machineInstanceOverlay: StatefulOverlay = {
  mutations: { CancelMachineInstance: cancelMachineInstance },
  queries: { MachineInstance: readMachineInstance },
};

export const manifestOverlay: StatefulOverlay = {
  mutations: {
    UpdateManifest: updateManifest,
    EnableManifest: enableManifest,
    DisableManifest: disableManifest,
    TriggerManifest: triggerManifest,
    SetManifestsEnabled: setManifestsEnabled,
    SetManifestsReplayDecisionsOnRetry: setManifestsReplayDecisionsOnRetry,
    TriggerManifests: triggerManifests,
  },
  queries: { Manifests: readManifests, ManifestDetail: readManifestDetail },
};

export const groupOverlay: StatefulOverlay = {
  mutations: {
    UpdateManifestGroup: updateManifestGroup,
    TriggerGroup: triggerGroup,
    CancelGroup: cancelGroup,
    SetManifestGroupsEnabled: setManifestGroupsEnabled,
    SetAllManifestGroupsEnabled: setAllManifestGroupsEnabled,
    TriggerGroups: triggerGroups,
    CancelGroups: cancelGroups,
  },
  queries: { ManifestGroups: readManifestGroups, ManifestGroupDetail: readManifestGroupDetail },
};

export const schedulerOverlay: StatefulOverlay = {
  mutations: { UpdateScheduler: updateScheduler, SetLogLevels: setLogLevels },
  queries: { SchedulerConfig: readSchedulerConfig, LogLevels: readLogLevels },
};

export const effectOverlay: StatefulOverlay = {
  mutations: { SetEffectEnabled: setEffectEnabled, ConfigureEffect: configureEffect },
  queries: { Effects: readEffects },
};

export const persistedOperationOverlay: StatefulOverlay = {
  mutations: {
    UploadPersistedOperation: uploadPersistedOperation,
    DeactivatePersistedOperation: deactivatePersistedOperation,
    RestorePersistedOperation: restorePersistedOperation,
  },
  queries: {
    PersistedOperations: readPersistedOperations,
    PersistedOperationDetail: readPersistedOperationDetail,
  },
};

/**
 * A host that does not call AddScheduler registers no log level service: setLogLevels refuses, in
 * the API's words. Layer it over the defaults (a story's `parameters.overlays`).
 */
export const noLogLevelServiceOverlay: StatefulOverlay = {
  mutations: {
    SetLogLevels: () =>
      wrap("operations.config.setLogLevels", {
        success: false,
        count: 0,
        notApplied: [],
        message:
          "This host registers no log level service, so log levels cannot be changed at runtime. AddScheduler(...) registers one.",
      }),
  },
};

/** The overlays applied by default. */
export const defaultOverlays: StatefulOverlay[] = [
  workQueueOverlay,
  deadLetterOverlay,
  executionOverlay,
  manifestOverlay,
  groupOverlay,
  schedulerOverlay,
  effectOverlay,
  persistedOperationOverlay,
  machineInstanceOverlay,
];

/**
 * Subscription operation names the mock drives itself (via the store event bus + simulator),
 * rather than forwarding to the auto-mock schema, which cannot execute subscriptions.
 */
export const mockedSubscriptions = new Set<string>([
  "OnTrainStateChanged",
  "OnDataChanged",
  "OnJunctionEvent",
]);
