export type Scenario = "RESEARCH" | "REFUND";

export type StepKind = "JUNCTION" | "CHOICE" | "SCORE" | "YES_NO" | "ROUTE";

export interface Step {
  position: number;
  kind: StepKind;
  name: string;
  state: "IN_PROGRESS" | "COMPLETED" | "FAILED" | "CANCELLED";
  startedAt: string;
  endedAt: string | null;
  durationMs: number | null;
  failureClass: string | null;
  failureException: string | null;
  questionKey: string | null;
  answer: string | null;
  confidence: number | null;
  replayed: boolean;
  answerWithheld: boolean;
  nameWithheld: boolean;
  trackPosition: number | null;
  attempt: number | null;
}

export interface JournalEntry {
  questionKey: string;
  occurrence: number;
  replayed: boolean;
  replayRefused: string | null;
  model: string | null;
  stateHash: string | null;
}

export interface Journal {
  replayDecisionsOf: number | null;
  replayAbandoned: boolean;
  decisions: JournalEntry[];
}

export interface Attempt {
  id: number;
  /** "retry" for the manifest's runs, "requeue" for a requeueExecution. */
  origin: "manifest" | "requeue";
  /** Set when the requeue asked afresh on purpose. */
  askedAfresh: boolean;
  trainState: string;
  startTime: string;
  endTime: string | null;
  failureJunction: string | null;
  failureReason: string | null;
  steps: Record<number, Step>;
  journal: Journal | null;
}

export interface RunInfo {
  runId: string;
  manifestId: number;
  manifestExternalId: string;
  trainName: string;
  armedCrash: string;
  maxRetries: number;
  scenario: Scenario;
}

export type Tone = "info" | "model" | "replay" | "error" | "success" | "system";

export interface ConsoleLine {
  key: string;
  at: string;
  tone: Tone;
  text: string;
}

export type Phase = "idle" | "starting" | "running" | "backoff" | "retrying" | "done" | "dead";
