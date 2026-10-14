import { getHideAdminTrains } from "../lib/adminTrains";
import type { MockStore } from "../mock/store/mock-store";
import type { RecordedFrame, Recordings } from "./recordings";

// Plays the recorded subscription frames into the mock store's event bus, which the stateful
// exchange relays to every open subscription of that name: the live feed, the change signals that
// make pages refetch, and a run's junction steps.
//
// The two host-wide streams loop at the pace they were recorded, each event stamped with the time it
// is played. A run's junction events are played once, for a run the recording caught in progress.
// While admin trains are hidden, their state changes are not played, so neither the live feed nor a
// failure toast shows the scheduler's own trains.

const LOOP_GAP_MS = 5_000;
const TERMINAL = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

function stamp(data: Record<string, unknown>, now: string): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(data))
    out[k] = v && typeof v === "object" && "timestamp" in (v as object) ? { ...(v as object), timestamp: now } : v;
  return out;
}

type Plays = (data: Record<string, unknown>) => boolean;

function loop(
  store: MockStore,
  name: string,
  frames: RecordedFrame[],
  timers: Set<ReturnType<typeof setTimeout>>,
  plays: Plays = () => true,
) {
  if (frames.length === 0) return;
  const start = frames[0].t;
  const period = frames[frames.length - 1].t - start + LOOP_GAP_MS;
  const play = () => {
    for (const frame of frames) {
      const timer = setTimeout(() => {
        timers.delete(timer);
        if (plays(frame.data)) store.publishEvent(name, stamp(frame.data, new Date().toISOString()));
      }, frame.t - start);
      timers.add(timer);
    }
    const again = setTimeout(() => {
      timers.delete(again);
      play();
    }, period);
    timers.add(again);
  };
  play();
}

/** The scheduler's own train names, as the recorded host listed them. */
export function recordedAdminTrainNames(recordings: Recordings): Set<string> {
  const answers = Object.values(recordings.queries.AdminTrainNames ?? {}) as {
    operations?: { adminTrainNames?: string[] };
  }[];
  return new Set(answers.flatMap((a) => a.operations?.adminTrainNames ?? []));
}

/** Starts the replay; returns a function that stops it. */
export function startReplay(
  store: MockStore,
  recordings: Recordings,
  hideAdmin: () => boolean = getHideAdminTrains,
): () => void {
  const timers = new Set<ReturnType<typeof setTimeout>>();
  const { OnTrainStateChanged, OnDataChanged, OnJunctionEvent } = recordings.subscriptions;
  const admin = recordedAdminTrainNames(recordings);
  const notAdmin: Plays = (data) => {
    const name = (data.onTrainStateChanged as { trainName?: string } | undefined)?.trainName;
    return !hideAdmin() || !name || !admin.has(name);
  };
  loop(store, "OnTrainStateChanged", OnTrainStateChanged, timers, notAdmin);
  loop(store, "OnDataChanged", OnDataChanged, timers);

  const details = recordings.queries.ExecutionDetail ?? {};
  for (const [metadataId, frames] of Object.entries(OnJunctionEvent)) {
    const detail = details[JSON.stringify({ id: Number(metadataId) })] as
      | { operations: { executionDetail: { trainState: string } | null } }
      | undefined;
    const state = detail?.operations.executionDetail?.trainState;
    if (!state || TERMINAL.has(state) || frames.length === 0) continue;
    const start = frames[0].t;
    for (const frame of frames) {
      const timer = setTimeout(() => store.publishEvent("OnJunctionEvent", frame.data), frame.t - start);
      timers.add(timer);
    }
  }

  return () => {
    for (const timer of timers) clearTimeout(timer);
    timers.clear();
  };
}
