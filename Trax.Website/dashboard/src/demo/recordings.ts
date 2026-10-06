// The demo's data: what three Trax sample hosts answered to every request the dashboard can send,
// recorded by Trax.Samples' scripts/recordings/dashboard.mjs into data/dashboard-recordings.json.
// See that script's header for what was recorded and how the hosts were combined.

/** One recorded write: the variables it was sent with and what the host answered. */
export interface RecordedMutation {
  variables: Record<string, unknown>;
  /** Sent to rows in the state the snapshot shows, so its answer holds for the demo too. */
  current?: boolean;
  data?: Record<string, unknown> | null;
  errors?: { message: string }[];
}

/** One subscription frame, `t` milliseconds after the first frame of the recording. */
export interface RecordedFrame {
  t: number;
  data: Record<string, unknown>;
}

export interface Recordings {
  /** When the hosts stopped changing; every time in the data is moved so this is now. */
  recordedAt: string;
  /** Query results by operation name, then by the variables' canonical key (see mock/variables-hash). */
  queries: Record<string, Record<string, unknown>>;
  mutations: Record<string, RecordedMutation[]>;
  subscriptions: {
    OnTrainStateChanged: RecordedFrame[];
    OnDataChanged: RecordedFrame[];
    OnJunctionEvent: Record<string, RecordedFrame[]>;
  };
}

/** The file as written: rows that repeat across pages are stored once, in `objects`, and referenced. */
export interface RecordingsFile extends Recordings {
  objects: unknown[];
}

function rehydrate(value: unknown, objects: unknown[]): unknown {
  if (Array.isArray(value)) return value.map((v) => rehydrate(v, objects));
  if (value && typeof value === "object") {
    const rec = value as Record<string, unknown>;
    const refs = rec.$refs;
    if (Array.isArray(refs) && Object.keys(rec).length === 1) return refs.map((i) => objects[i as number]);
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(rec)) out[k] = rehydrate(v, objects);
    return out;
  }
  return value;
}

const ISO = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/;

// Move every timestamp by `deltaMs`, keeping the precision the host sent (some are whole seconds).
function shift(value: unknown, deltaMs: number): unknown {
  if (typeof value === "string") {
    if (!ISO.test(value)) return value;
    return new Date(Date.parse(value) + deltaMs).toISOString();
  }
  if (Array.isArray(value)) return value.map((v) => shift(v, deltaMs));
  if (value && typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value)) out[k] = shift(v, deltaMs);
    return out;
  }
  return value;
}

/**
 * The recordings as the demo serves them: references resolved, and every time moved forward by
 * whole minutes so the last moment recorded is about `now` (minute and hour buckets stay aligned).
 */
export function prepareRecordings(file: RecordingsFile, now: number = Date.now()): Recordings {
  const deltaMs = Math.floor((now - Date.parse(file.recordedAt)) / 60_000) * 60_000;
  const objects = shift(file.objects, deltaMs) as unknown[];
  return {
    recordedAt: new Date(Date.parse(file.recordedAt) + deltaMs).toISOString(),
    queries: rehydrate(shift(file.queries, deltaMs), objects) as Recordings["queries"],
    mutations: shift(file.mutations, deltaMs) as Recordings["mutations"],
    subscriptions: shift(file.subscriptions, deltaMs) as Recordings["subscriptions"],
  };
}
