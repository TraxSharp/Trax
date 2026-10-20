// The ingest twin, generated from the IR the C# test machine (IngestMachine) exports. Its Fetching state invokes
// a train; the twin applies that train's outcomes (Fetching.done, .failed, .cancelled) as events and never starts
// the train. It exists to prove the outcome triggers through the differential corpus.

import { ingest } from "./ingest.machine.g";

export { ingest };
export type {
  IngestOutcome,
  IngestSpec,
  IngestState,
  IngestTrigger,
} from "./ingest.contexts.g";

export const ingestCore = ingest.core;
