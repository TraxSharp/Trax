import type { MockStore } from "./store/mock-store";

// Synthetic train-lifecycle event generator. Publishes onTrainStateChanged events to the
// store event bus on an interval so the Executions live feed, the Overview refresh nudge, and
// the failure NotificationBridge all animate with no backend. The statefulExchange relays
// these to any open OnTrainStateChanged subscription.

const TRAINS = [
  "Trax.Demo.Trains.OrderTrain",
  "Trax.Demo.Trains.EmailTrain",
  "Trax.Demo.Trains.ReportTrain",
];
const STATES = ["PENDING", "IN_PROGRESS", "COMPLETED", "FAILED"] as const;

export interface SimulatorOptions {
  intervalMs?: number;
}

/** Start emitting synthetic events; returns a stop function. */
export function startTrainEventSimulator(
  store: MockStore,
  opts: SimulatorOptions = {},
): () => void {
  const intervalMs = opts.intervalMs ?? 2000;
  let n = 0;

  const timer = setInterval(() => {
    n += 1;
    const trainName = TRAINS[n % TRAINS.length];
    const trainState = STATES[n % STATES.length];
    const failed = trainState === "FAILED";
    store.publishEvent("OnTrainStateChanged", {
      onTrainStateChanged: {
        metadataId: 900_000 + n,
        externalId: `sim-${n}`,
        trainName,
        trainState,
        timestamp: new Date().toISOString(),
        failureJunction: failed ? "ValidateStep" : null,
        failureReason: failed ? "Simulated failure for the offline demo" : null,
      },
    });
  }, intervalMs);

  return () => clearInterval(timer);
}
