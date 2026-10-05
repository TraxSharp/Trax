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
  /** What started it, when it was not the first run or the manifest's own retry. */
  startedBy: "askAfresh" | "requeue" | null;
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
  /** Milliseconds since Run was pressed. */
  t: number;
  tone: Tone;
  text: string;
}

export type Phase = "idle" | "starting" | "running" | "backoff" | "retrying" | "done" | "requeue" | "dead";

/** What the reader did during the backoff, if anything. */
export type Fork = "none" | "askAfresh" | "changeData";

/** An answer as the page shows it: a probability or score to two places, a choice as it is. */
export const shownAnswer = (answer: string | null) =>
  answer != null && /^-?\d+\.\d{3,}$/.test(answer) ? Number(answer).toFixed(2) : answer;
