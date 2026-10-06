import { hashVariables } from "../mock/variables-hash";
import type { Recordings } from "./recordings";

// How the demo answers a query from the recordings.
//
//   recorded  the host's answer for exactly these variables.
//   derived   a text, time or id filter typed into a list, which no recording can enumerate: the
//             recorded list without that filter, filtered here the way the API filters it.
//   created   a detail for a row this session created (a queued train, a run): the API's "not
//             found" shape, which the overlays fill in.
//   missing   nothing recorded. The demo answers with an error rather than invent data.

export type Served = "recorded" | "derived" | "created" | "missing";

export interface LookupResult {
  served: Served;
  data?: Record<string, unknown>;
}

type Rec = Record<string, unknown>;

// Where each keyset list keeps its page.
const PAGES: Record<string, string[]> = {
  Executions: ["operations", "executions"],
  ExecutionChildren: ["operations", "executionChildren"],
  WorkQueue: ["operations", "workQueue", "workQueues"],
  DeadLetters: ["operations", "deadLetters", "deadLetters"],
  Logs: ["operations", "logs", "logs"],
  Manifests: ["operations", "manifests"],
  ManifestGroups: ["operations", "manifestGroups", "groups"],
};

const lower = (v: unknown) => String(v ?? "").toLowerCase();

// The filters a person types, and how the API applies each (see the Trax.Api operations queries:
// names and keys match exactly, logs' text matches anywhere ignoring case).
const FREE_FILTERS: Record<string, Record<string, (item: Rec, value: unknown) => boolean>> = {
  Executions: {
    trainName: (i, v) => i.name === v,
    externalId: (i, v) => i.externalId === v,
    hostName: (i, v) => i.hostName === v,
    parentId: (i, v) => i.parentId === v,
    startedAfter: (i, v) => Date.parse(String(i.startTime)) >= Date.parse(String(v)),
    startedBefore: (i, v) => Date.parse(String(i.startTime)) <= Date.parse(String(v)),
  },
  WorkQueue: {
    trainName: (i, v) => i.trainName === v,
    subjectKey: (i, v) => i.subjectKey === v,
  },
  Manifests: { nameContains: (i, v) => String(i.name).includes(String(v)) },
  ManifestGroups: { nameContains: (i, v) => String(i.name).includes(String(v)) },
  Logs: {
    metadataId: (i, v) => i.metadataId === v,
    messageContains: (i, v) => lower(i.message).includes(lower(v)),
    categoryContains: (i, v) => lower(i.category).includes(lower(v)),
  },
};

// The API's answer for an id nothing has: null, in the detail's own shape.
const NOT_FOUND: Record<string, () => Rec> = {
  ExecutionDetail: () => ({ operations: { executionDetail: null } }),
  WorkQueueDetail: () => ({ operations: { workQueue: { detail: null } } }),
  DeadLetterDetail: () => ({ operations: { deadLetters: { deadLetter: null } } }),
  ManifestDetail: () => ({ operations: { manifestDetail: null } }),
  JunctionRuns: () => ({ operations: { junctionRuns: [] } }),
  Decisions: () => ({ operations: { decisions: { items: [], take: 26, nextCursor: null } } }),
  Logs: () => ({
    operations: {
      logs: { logs: { items: [], totalCount: 0, isEstimatedCount: false, isCountCapped: false, nextCursor: null } },
    },
  }),
  PersistedOperationDetail: () => ({
    operations: { persistedOperations: { persistedOperation: null, persistedOperationHistory: [] } },
  }),
};

/** Ids at or above this were made by this session's writes (the overlays number from 9,000,000). */
export const SESSION_ID_FLOOR = 9_000_000;

function getAt(data: unknown, path: string[]): unknown {
  return path.reduce<unknown>((acc, key) => (acc && typeof acc === "object" ? (acc as Rec)[key] : undefined), data);
}

function setAt(path: string[], value: unknown): Rec {
  return path.reduceRight<Rec>((acc, key) => ({ [key]: acc }), value as Rec);
}

function without(vars: Rec, keys: string[]): Rec {
  return Object.fromEntries(Object.entries(vars).filter(([k]) => !keys.includes(k)));
}

/** Every row of a recorded list, read page by page from its first page at this page size. */
function wholeList(byHash: Record<string, unknown>, op: string, vars: Rec): { items: Rec[]; envelope: Rec } | null {
  const path = PAGES[op];
  const items: Rec[] = [];
  let envelope: Rec | null = null;
  let afterId: unknown = null;
  for (let i = 0; i < 1000; i++) {
    const data = byHash[hashVariables({ ...vars, afterId })];
    if (data === undefined) return envelope ? { items, envelope } : null;
    const page = getAt(data, path) as Rec;
    envelope ??= page;
    const rows = (page.items as Rec[]) ?? [];
    items.push(...rows);
    if (rows.length === 0 || page.nextCursor == null) break;
    afterId = page.nextCursor;
  }
  return envelope ? { items, envelope } : null;
}

function deriveList(recordings: Recordings, op: string, vars: Rec): Rec | undefined {
  const filters = FREE_FILTERS[op];
  const byHash = recordings.queries[op];
  if (!filters || !byHash) return undefined;
  const applied = Object.keys(filters).filter((k) => vars[k] != null && vars[k] !== "");
  if (applied.length === 0) return undefined;
  const base = without(vars, [...applied, "afterId"]);
  const whole = wholeList(byHash, op, base);
  if (!whole) return undefined;
  const matching = whole.items.filter((item) => applied.every((k) => filters[k](item, vars[k])));
  const take = Number(vars.take);
  const start = vars.afterId == null ? 0 : matching.findIndex((i) => i.id === vars.afterId) + 1;
  const page = matching.slice(start, start + take);
  return setAt(PAGES[op], {
    ...whole.envelope,
    items: page,
    totalCount: matching.length,
    isEstimatedCount: false,
    nextCursor: page.length > 0 ? page[page.length - 1].id : null,
  });
}

// Persisted operations page by offset; an id prefix nobody recorded is cut from the unfiltered list.
function derivePersistedOperations(recordings: Recordings, vars: Rec): Rec | undefined {
  const filter = (vars.filter as Rec | undefined) ?? {};
  const prefix = filter.idStartsWith;
  if (prefix == null || prefix === "") return undefined;
  const data = recordings.queries.PersistedOperations?.[
    hashVariables({ ...vars, skip: 0, filter: { ...filter, idStartsWith: null } })
  ];
  if (!data) return undefined;
  const path = ["operations", "persistedOperations", "persistedOperations"];
  const page = getAt(data, path) as Rec;
  const items = (page.items as Rec[]).filter((i) => String(i.id).startsWith(String(prefix)));
  const skip = Number(vars.skip ?? 0);
  return setAt(path, { ...page, items: items.slice(skip, skip + Number(vars.take)), totalCount: items.length });
}

function idsIn(vars: Rec): number[] {
  return ["id", "metadataId", "parentId", "manifestId"]
    .map((k) => vars[k])
    .filter((v): v is number => typeof v === "number");
}

/** Answers one query from the recordings; never invents data. */
export function lookupQuery(recordings: Recordings, op: string, variables: Rec): LookupResult {
  const vars = variables ?? {};
  const byHash = recordings.queries[op];
  const exact = byHash?.[hashVariables(vars)];
  if (exact !== undefined) return { served: "recorded", data: structuredClone(exact) as Rec };

  const derived = op === "PersistedOperations" ? derivePersistedOperations(recordings, vars) : deriveList(recordings, op, vars);
  if (derived) return { served: "derived", data: derived };

  const notFound = NOT_FOUND[op];
  if (notFound && idsIn(vars).some((id) => id >= SESSION_ID_FLOOR)) return { served: "created", data: notFound() };
  // A persisted operation this session uploaded has no recorded detail either.
  if (op === "PersistedOperationDetail") return { served: "created", data: notFound!() };

  return { served: "missing" };
}

/** The recorded answers to a write, the closest first: the same variables, then any. */
export function recordedMutations(recordings: Recordings, op: string, variables: Rec) {
  const all = recordings.mutations[op] ?? [];
  const key = hashVariables(variables);
  const exact = all.find((m) => hashVariables(m.variables) === key);
  return { exact, all };
}
