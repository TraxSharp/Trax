import { enumLabel, formatTime } from "./format";

// Bulk operations (api/0045): one action over every row a list's filter matches, run in batches on
// the server and read back by id. The filter is the list read's own, taken from the page's URL.

export type BulkOperationKind = "CANCEL" | "REQUEUE" | "ACKNOWLEDGE" | "TRIGGER" | "ENABLE" | "DISABLE";
export type BulkOperationStatus = "RUNNING" | "FINISHED" | "FAILED" | "CANCELED";

type Value = string | number | boolean | null | undefined;

export interface BulkOperationFilter {
  executions?: Record<string, Value> | null;
  deadLetters?: Record<string, Value> | null;
  manifests?: Record<string, Value> | null;
}

export interface BulkOperationSummary {
  id: string;
  kind: BulkOperationKind;
  status: BulkOperationStatus;
  actor: string;
  askAfresh: boolean;
  startedAt: string;
  finishedAt: string | null;
  total: number;
  done: number;
  failed: number;
  message: string;
}

export interface BulkOperationDetail extends BulkOperationSummary {
  filter: BulkOperationFilter;
  failureReasons: { reason: string; count: number }[];
  failures: { id: number; reason: string }[];
}

export interface StartBulkOperationResponse {
  success: boolean;
  message: string;
  matched: number | null;
  operation: BulkOperationSummary | null;
}

/** What a kind does, to which list, in the words its button and its page use. */
export const BULK_KINDS: Record<BulkOperationKind, { verb: string; noun: string; list: string; path: string }> = {
  CANCEL: { verb: "Cancel", noun: "runs", list: "Runs", path: "/runs" },
  REQUEUE: { verb: "Requeue", noun: "dead letters", list: "Dead letters", path: "/runs/dead-letters" },
  ACKNOWLEDGE: { verb: "Acknowledge", noun: "dead letters", list: "Dead letters", path: "/runs/dead-letters" },
  TRIGGER: { verb: "Queue", noun: "manifests", list: "Manifests", path: "/schedules" },
  ENABLE: { verb: "Enable", noun: "manifests", list: "Manifests", path: "/schedules" },
  DISABLE: { verb: "Disable", noun: "manifests", list: "Manifests", path: "/schedules" },
};

/** "Cancel runs", "Queue manifests". */
export function bulkTitle(kind: BulkOperationKind): string {
  const k = BULK_KINDS[kind];
  return `${k.verb} ${k.noun}`;
}

/** Where one id of a kind's list opens. */
export function bulkItemPath(kind: BulkOperationKind, id: number): string {
  return `${BULK_KINDS[kind].path}/${id}`;
}

// The filter's fields as the list pages name them, and the URL parameter each page reads them from.
const FIELDS: Record<keyof BulkOperationFilter, [field: string, label: string, param?: string][]> = {
  executions: [
    ["trainName", "Train", "train"],
    ["trainState", "State", "state"],
    ["failureClass", "Failure class", "failure"],
    ["startedAfter", "Started after", "after"],
    ["startedBefore", "Started before", "before"],
    ["externalId", "External id", "externalId"],
    ["parentId", "Parent run", "parentId"],
    ["hostName", "Host", "host"],
    ["manifestId", "Manifest"],
    ["manifestGroupId", "Manifest group"],
    ["failureReasonContains", "Failure reason contains"],
    ["failureJunction", "Failed at junction"],
    ["hideAdminTrains", "Admin trains hidden"],
  ],
  deadLetters: [
    ["status", "Status", "status"],
    ["manifestId", "Manifest", "manifestId"],
  ],
  manifests: [
    ["nameContains", "Name contains", "name"],
    ["scheduleType", "Schedule", "schedule"],
    ["isEnabled", "Enabled", "enabled"],
    ["manifestGroupId", "Manifest group"],
    ["hideAdminTrains", "Admin trains hidden"],
  ],
};

const ENUM_FIELDS = new Set(["trainState", "failureClass", "status", "scheduleType"]);
const TIME_FIELDS = new Set(["startedAfter", "startedBefore"]);

function listOf(filter: BulkOperationFilter): keyof BulkOperationFilter | null {
  return (["executions", "deadLetters", "manifests"] as const).find((k) => filter[k] != null) ?? null;
}

/** The filter's set fields, each with its label and its value as the list page shows it. */
export function describeFilter(filter: BulkOperationFilter): { label: string; value: string }[] {
  const list = listOf(filter);
  if (!list) return [];
  const given = filter[list]!;
  return FIELDS[list]
    .filter(([field]) => given[field] != null && given[field] !== "")
    .map(([field, label]) => {
      const v = given[field]!;
      const value =
        typeof v === "boolean"
          ? v
            ? "Yes"
            : "No"
          : ENUM_FIELDS.has(field)
            ? enumLabel(String(v))
            : TIME_FIELDS.has(field)
              ? formatTime(String(v))
              : String(v);
      return { label, value };
    });
}

// "2026-10-19T09:30:00Z" as the datetime-local value the Runs page keeps in its URL.
function localInput(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * The list page with the filter in its URL, as near as the page can show it: a field the page has no
 * control for (a failure reason, a manifest group) is left out, and so is "hide admin trains", which
 * is the viewer's own preference.
 */
export function filterListPath(kind: BulkOperationKind, filter: BulkOperationFilter): string {
  const list = listOf(filter);
  const params = new URLSearchParams();
  if (list) {
    const given = filter[list]!;
    for (const [field, , param] of FIELDS[list]) {
      const v = given[field];
      if (!param || v == null || v === "") continue;
      params.set(param, TIME_FIELDS.has(field) ? localInput(String(v)) : String(v));
    }
  }
  const query = params.toString();
  return `${BULK_KINDS[kind].path}${query ? `?${query}` : ""}`;
}

/** The ids attempted so far, done or not. */
export function attempted(op: Pick<BulkOperationSummary, "done" | "failed">): number {
  return op.done + op.failed;
}

/** Tailwind classes for a status pill. */
export const BULK_STATUS_STYLE: Record<BulkOperationStatus, string> = {
  RUNNING: "bg-info-soft text-info-fg",
  FINISHED: "bg-ok-soft text-ok-fg",
  FAILED: "bg-danger-soft text-danger-fg",
  CANCELED: "bg-warn-soft text-warn-fg",
};
