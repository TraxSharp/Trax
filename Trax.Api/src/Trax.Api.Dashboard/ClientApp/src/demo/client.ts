import { TraxTransportError, type TraxTransport } from "../trax/index.g";
import { ConsoleClient } from "../lib/consoleClient";
import type { MockStore } from "../mock/store/mock-store";
import { statefulTransport } from "../mock/store/stateful-transport";
import { defaultOverlays, type MutationOverlay, type StatefulOverlay } from "../mock/store/overlays";
import { lookupQuery, recordedMutations, type Served } from "./lookup";
import type { RecordedMutation, Recordings } from "./recordings";

type Rec = Record<string, unknown>;

/** Told about every query the demo answers, and how; the strict tests collect these. */
export type ServeListener = (op: string, variables: Rec, served: Served) => void;

export const NO_RECORDING = "This demo has no recording for this view.";

// A write the host refused answers success: false, or with errors, somewhere in its payload.
function refused(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(refused);
  if (value && typeof value === "object") {
    const rec = value as Rec;
    if (rec.success === false) return true;
    if (Array.isArray(rec.errors) && rec.errors.length > 0) return true;
    return Object.values(rec).some(refused);
  }
  return false;
}

// Puts the host's wording on the overlay's answer. A message from another row's write is used only
// when it names no row (no digits), so it never mentions the wrong id or count.
function withMessages(ack: unknown, recorded: unknown, anyMessage: boolean): unknown {
  if (!ack || typeof ack !== "object" || !recorded || typeof recorded !== "object") return ack;
  if (Array.isArray(ack)) return ack;
  const out: Rec = { ...(ack as Rec) };
  const rec = recorded as Rec;
  for (const [k, v] of Object.entries(out)) {
    if (k === "message" && typeof v === "string" && typeof rec[k] === "string") {
      const message = rec[k] as string;
      if (anyMessage || !/\d/.test(message)) out[k] = message;
    } else if (v && typeof v === "object") out[k] = withMessages(v, rec[k], anyMessage);
  }
  return out;
}

/**
 * The default overlays, answering in the host's words. A write the host refused, recorded with the
 * same variables against the rows the snapshot shows ("current"), is refused here too and changes
 * nothing; otherwise the overlay makes the change (so every page reads it back) and its answer
 * carries the host's message: the current recording's, or any recording's that names no row.
 */
export function recordedWordingOverlays(recordings: Recordings, overlays = defaultOverlays): StatefulOverlay[] {
  return overlays.map((overlay) => {
    if (!overlay.mutations) return overlay;
    const mutations: Record<string, MutationOverlay> = {};
    for (const [op, write] of Object.entries(overlay.mutations)) {
      mutations[op] = (variables, store, context) => {
        const { exact, all } = recordedMutations(recordings, op, variables);
        const current = exact?.current ? exact : undefined;
        if (current?.data && refused(current.data)) return current.data as Rec;
        const like = (m: RecordedMutation) => m.data && !refused(m.data);
        const source = current && like(current) ? current : all.find(like);
        const worded = (ack: Rec) => (source ? (withMessages(ack, source.data, source === current) as Rec) : ack);
        const ack = write(variables, store, context);
        return ack instanceof Promise ? ack.then(worded) : worded(ack);
      };
    }
    return { ...overlay, mutations };
  });
}

/**
 * The end of the demo's transport: answers queries from the recordings (see lookup.ts) and a write
 * no overlay handles from its recorded answer. Nothing reaches a network.
 */
export function recordingTransport(recordings: Recordings, onServe?: ServeListener): TraxTransport {
  return {
    async execute({ document, variables }) {
      const name = document.name;
      const vars = { ...variables } as Rec;
      if (document.kind === "query") {
        const result = lookupQuery(recordings, name, vars);
        onServe?.(name, vars, result.served);
        if (!result.data) throw new TraxTransportError(NO_RECORDING);
        return { data: result.data };
      }
      const { exact } = recordedMutations(recordings, name, vars);
      onServe?.(name, vars, exact?.data ? "recorded" : "missing");
      if (!exact?.data) throw new TraxTransportError(NO_RECORDING);
      return { data: exact.data };
    },
    subscribe({ document, variables }, sink) {
      onServe?.(document.name, { ...variables } as Rec, "missing");
      sink.error(new TraxTransportError(NO_RECORDING));
      return () => {};
    },
  };
}

export interface DemoClientOptions {
  recordings: Recordings;
  store: MockStore;
  onServe?: ServeListener;
  /** Overlays layered over the recorded-wording defaults (a story's `parameters.overlays`). */
  overlays?: StatefulOverlay[];
}

/** A console client that answers from the recordings, with the mock's overlays for writes. */
export function createDemoClient({ recordings, store, onServe, overlays = [] }: DemoClientOptions): ConsoleClient {
  const stateful = statefulTransport(store, [...recordedWordingOverlays(recordings), ...overlays], () => top);
  const top: TraxTransport = stateful(recordingTransport(recordings, onServe));
  const client = new ConsoleClient(top);
  // A page does not offer what no recording answers (see lib/answerable.ts).
  client.limitAnswers(Object.keys(recordings.queries));
  return client;
}
