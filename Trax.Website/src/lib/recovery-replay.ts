// Plays back the Recovery sample's recorded runs (src/data/recovery-recordings.json, written by Trax.Samples'
// scripts/recordings). A recording is what the sample's own page received from the host, with the time each
// thing arrived: the manifest's attempts, every junction event, each attempt's decision journal. Applying the
// events in order rebuilds what that page showed, and narrates it the same way.
//
// A crashing run forks during its backoff (ask afresh, or change the data) and every run forks once it is over
// (ask afresh again, as a requeue). Each fork was recorded as a run of its own, marked where it forked, and the
// recordings number their executions the same way, so switching to another at the fork changes nothing on screen.

export type Scenario = "RESEARCH" | "REFUND";
export type Fork = "none" | "askAfresh" | "changeData";
export type StepKind = "JUNCTION" | "CHOICE" | "SCORE" | "YES_NO" | "ROUTE";
export type StepState = "IN_PROGRESS" | "COMPLETED" | "FAILED" | "CANCELLED";

/** One junction, question or route of an attempt. Times are milliseconds from the recording's start. */
export interface Step {
  position: number;
  kind: StepKind;
  name: string;
  state: StepState;
  start?: number;
  end?: number;
  durationMs?: number;
  failureException?: string;
  questionKey?: string;
  answer?: string;
  confidence?: number;
  replayed?: boolean;
}

export interface Row {
  id: number;
  trainState: string;
  start?: number;
  end?: number;
  failureJunction?: string;
}

export interface Journal {
  replayDecisionsOf?: number;
  replayAbandoned?: boolean;
  decisions: { questionKey: string; replayed?: boolean; replayRefused?: string; model?: string }[];
}

export interface RunInfo {
  trainName: string;
  armedCrash: string;
  maxRetries: number;
}

export type ReplayEvent = { t: number } & (
  | { type: "started"; run: RunInfo }
  | { type: "attempt"; row: Row; origin: "manifest" | "requeue" }
  | { type: "row"; row: Row }
  | { type: "step"; attemptId: number; step: Step }
  | { type: "journal"; attemptId: number; journal: Journal }
  | { type: "fork"; at: "backoff" | "done" }
  | { type: "action"; action: "askAfresh" | "changeData" | "requeue"; change?: string; message?: string; sourceId?: number }
  | { type: "end" }
);

export interface Recording {
  key: string;
  scenario: Scenario;
  topic?: string;
  orderId?: string;
  crashOnce?: boolean;
  fork: Fork;
  events: ReplayEvent[];
}

export interface Attempt {
  id: number;
  origin: "manifest" | "requeue";
  /** What started it, when it was not the first run or the manifest's own retry. */
  startedBy: "askAfresh" | "requeue" | null;
  trainState: string;
  start?: number;
  end?: number;
  failureJunction?: string;
  steps: Record<number, Step>;
  journal: Journal | null;
}

export type Tone = "info" | "model" | "replay" | "error" | "success" | "system";

export interface Line {
  key: string;
  t: number;
  tone: Tone;
  text: string;
}

export interface Playback {
  recording: Recording;
  /** The next event to apply. */
  cursor: number;
  run: RunInfo | null;
  attempts: Attempt[];
  lines: Line[];
  /** True once the run is over and playback waits for "ask afresh" (or for nothing more). */
  waiting: boolean;
  /** The fork taken during the backoff, if any. */
  forkTaken: Fork;
  requeued: boolean;
  finished: boolean;
}

export type Phase = "idle" | "starting" | "runs" | "breaks" | "recovers" | "done" | "requeue";

const RANK: Record<StepState, number> = { IN_PROGRESS: 0, COMPLETED: 1, FAILED: 1, CANCELLED: 1 };

/** The recording for a subject ("papers", "wiki" or an order id), with or without the crash, and a fork. */
export function keyOf(scenario: Scenario, subject: string, crash: boolean, fork: Fork = "none"): string {
  const base = `${scenario === "RESEARCH" ? "research" : "refund"}-${subject}`;
  return crash ? `${base}-crash-${fork}` : `${base}-clean`;
}

export function begin(recording: Recording): Playback {
  return {
    recording,
    cursor: 0,
    run: null,
    attempts: [],
    lines: [],
    waiting: false,
    forkTaken: "none",
    requeued: false,
    finished: false,
  };
}

/**
 * Applies every event up to `elapsed` milliseconds into the recording. Stops at the "done" fork: the run is
 * over, and what follows (a requeue that asks afresh) plays only when asked for.
 */
export function advance(playback: Playback, elapsed: number): Playback {
  const { events } = playback.recording;
  if (playback.waiting || playback.finished || playback.cursor >= events.length) return playback;
  if (events[playback.cursor].t > elapsed) return playback;

  const next: Playback = {
    ...playback,
    attempts: playback.attempts.map((a) => ({ ...a, steps: { ...a.steps } })),
    lines: [...playback.lines],
  };
  while (next.cursor < events.length && events[next.cursor].t <= elapsed) {
    const event = events[next.cursor++];
    apply(next, event);
    if (event.type === "fork" && event.at === "done") {
      next.waiting = true;
      break;
    }
    if (event.type === "end") next.finished = true;
  }
  return next;
}

/**
 * Takes a fork during the backoff: continues in the recording of the same run that forked that way. Returns the
 * new playback and how far its clock is ahead of the old one's at the fork, which the caller adds to its elapsed
 * time so the new recording's events land when they would have.
 */
export function takeFork(
  playback: Playback,
  recordings: Recording[],
  fork: Exclude<Fork, "none">,
): { playback: Playback; shift: number } | null {
  if (phaseOf(playback) !== "breaks" || playback.forkTaken !== "none") return null;
  const from = playback.recording;
  const target = recordings.find(
    (r) => r.scenario === from.scenario && r.topic === from.topic && r.orderId === from.orderId && r.crashOnce && r.fork === fork,
  );
  if (!target) return null;
  const fromFork = forkIndex(from, "backoff");
  const toFork = forkIndex(target, "backoff");
  if (fromFork < 0 || toFork < 0) return null;
  // The attempt's failure arrives a few milliseconds before its journal and the fork mark: catch up to the mark.
  if (playback.cursor <= fromFork) playback = advance(playback, from.events[fromFork].t);
  return {
    playback: { ...playback, recording: target, cursor: toFork + 1, forkTaken: fork },
    shift: target.events[toFork].t - from.events[fromFork].t,
  };
}

/** Asks afresh once the run is over: plays the recorded requeue. Returns the time its first event is due. */
export function requeue(playback: Playback): { playback: Playback; at: number } | null {
  if (!playback.waiting || playback.requeued) return null;
  const at = playback.recording.events[playback.cursor]?.t;
  if (at == null) return null;
  return { playback: { ...playback, waiting: false, requeued: true }, at };
}

function forkIndex(recording: Recording, at: "backoff" | "done"): number {
  return recording.events.findIndex((e) => e.type === "fork" && e.at === at);
}

export function phaseOf(playback: Playback | null): Phase {
  if (!playback) return "idle";
  const manifest = playback.attempts.filter((a) => a.origin === "manifest");
  const last = manifest[manifest.length - 1];
  const requeued = playback.attempts.find((a) => a.origin === "requeue");
  if (requeued) return requeued.journal ? "done" : "requeue";
  if (!last) return "starting";
  if (last.trainState === "COMPLETED") return last.journal ? "done" : "recovers";
  if (last.trainState === "FAILED") return "breaks";
  return manifest.length > 1 ? "recovers" : "runs";
}

/** The label the console and the timeline give an attempt. */
export function labelOf(playback: Playback, attempt: Attempt): string {
  if (attempt.origin === "requeue") return "[requeue]";
  const index = playback.attempts.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id);
  return `[attempt ${index + 1}]`;
}

/** Why a question's answer was or was not replayed, from the event and the attempt's decision journal. */
export function badgeOf(playback: Playback, attempt: Attempt, step: Step): { text: string; tone: string } | null {
  if (step.kind === "JUNCTION" || step.kind === "ROUTE") return null;
  if (step.replayed) return { text: "replayed: model not asked", tone: "replay" };
  const entry = attempt.journal?.decisions.find((d) => d.questionKey === step.questionKey);
  if (entry?.replayRefused) return { text: "asked afresh: state changed", tone: "afresh" };
  if (attempt.journal?.replayAbandoned) return { text: "asked afresh: replay abandoned", tone: "afresh" };
  const first = playback.attempts[0]?.id === attempt.id;
  if (!first)
    return attempt.journal && attempt.journal.replayDecisionsOf == null
      ? { text: "asked afresh: on purpose", tone: "afresh" }
      : { text: "asked afresh", tone: "afresh" };
  return { text: "model asked", tone: "model" };
}

/** The step the code panel follows: the running one, else the latest step of the latest attempt. */
export function currentStep(playback: Playback | null): Step | null {
  const last = playback?.attempts[playback.attempts.length - 1];
  if (!last) return null;
  const steps = Object.values(last.steps).sort((a, b) => a.position - b.position);
  return steps.find((s) => s.state === "IN_PROGRESS") ?? steps[steps.length - 1] ?? null;
}

const GATE_TRACKS: Record<string, string> = { Yes: ".Yes(", No: ".No(", Unsure: ".Unsure(" };

/**
 * The line (0-based) of the train's source that declares a step: `Chain<Name>` for a junction, the routing step
 * for a question, and the track's `.When(...)`, `.AtLeast(...)` or `.Yes(...)` for a route. -1 when not found.
 */
export function lineOf(code: string, step: Step): number {
  const lines = code.split("\n");
  const find = (needle: string) => lines.findIndex((l) => l.includes(needle));
  switch (step.kind) {
    case "JUNCTION":
      return find(`Chain<${step.name}>`);
    case "ROUTE": {
      const gate = step.answer ? GATE_TRACKS[step.answer] : undefined;
      if (gate) return find(gate);
      return find(`${step.questionKey}.${step.answer}`);
    }
    default:
      return find(`, ${step.questionKey}>`);
  }
}

function apply(p: Playback, event: ReplayEvent): void {
  const say = (key: string, t: number, tone: Tone, text: string) => {
    if (!p.lines.some((l) => l.key === key)) p.lines.push({ key, t, tone, text });
  };
  const attempt = (id: number) => p.attempts.find((a) => a.id === id);

  switch (event.type) {
    case "started": {
      p.run = event.run;
      const crash =
        event.run.armedCrash === "REPORT"
          ? ", with a crash armed in the report step of the first attempt"
          : event.run.armedCrash === "REFUND_TRACK"
            ? ", with a crash armed in the step after the approval, on the first attempt"
            : "";
      say("start", event.t, "system", `# Scheduled a one-off manifest (MaxRetries ${event.run.maxRetries})${crash}`);
      return;
    }
    case "attempt": {
      if (attempt(event.row.id)) return;
      const startedBy =
        event.origin === "requeue" ? "requeue" : p.forkTaken === "askAfresh" && p.attempts.length > 0 ? "askAfresh" : null;
      const a: Attempt = { ...event.row, origin: event.origin, startedBy, steps: {}, journal: null };
      p.attempts.push(a);
      const label = labelOf(p, a);
      const index = p.attempts.filter((x) => x.origin === "manifest").length - 1;
      const text =
        startedBy === "requeue"
          ? `RUN execution ${a.id} started by the requeue`
          : startedBy === "askAfresh"
            ? `RUN execution ${a.id} started by the trigger, without waiting out the backoff`
            : index === 0
              ? `RUN execution ${a.id} started`
              : `RETRY ${index}/${p.run?.maxRetries ?? 2}: the manifest's retry started as execution ${a.id}`;
      say(`${a.id}:start`, event.t, "system", `${label} # ${text}`);
      return;
    }
    case "row": {
      const a = attempt(event.row.id);
      if (!a) return;
      Object.assign(a, event.row);
      if (a.trainState === "FAILED" && a.origin === "manifest")
        say(
          `${a.id}:end`,
          event.t,
          "system",
          `${labelOf(p, a)} # FAILED. The scheduler retries after its backoff (a few seconds here), naming execution ${a.id} as the run to replay.`,
        );
      return;
    }
    case "step": {
      const a = attempt(event.attemptId);
      if (!a) return;
      const known = a.steps[event.step.position];
      if (known && RANK[known.state] >= RANK[event.step.state]) return;
      a.steps[event.step.position] = event.step;
      narrate(p, a, event.step, event.t, say);
      return;
    }
    case "journal": {
      const a = attempt(event.attemptId);
      if (!a) return;
      a.journal = event.journal;
      if (a.trainState === "COMPLETED")
        say(`${a.id}:end`, event.t, "success", `${labelOf(p, a)} # COMPLETED. ${describeJournal(event.journal)}`);
      return;
    }
    case "action": {
      const text =
        event.action === "changeData"
          ? `# DATA CHANGED during the backoff: ${event.change}`
          : event.action === "askAfresh"
            ? `# ASK AFRESH: triggerManifest(askAfresh: true) says "${event.message}"`
            : `# ASK AFRESH: requeueExecution(${event.sourceId}, askAfresh: true) says "${event.message}"`;
      say(`action:${event.action}`, event.t, "system", text);
      return;
    }
  }
}

function narrate(
  p: Playback,
  a: Attempt,
  step: Step,
  t: number,
  say: (key: string, t: number, tone: Tone, text: string) => void,
): void {
  const label = labelOf(p, a);
  const key = `${a.id}:${step.position}:${step.state}`;
  if (step.kind === "JUNCTION") {
    if (step.state === "IN_PROGRESS") say(key, t, "info", `${label} # JUNCTION ${step.name} is running...`);
    else if (step.state === "COMPLETED")
      say(key, t, "info", `${label} # JUNCTION ${step.name} completed in ${step.durationMs ?? 0} ms`);
    else
      say(
        key,
        t,
        "error",
        `${label} # JUNCTION ${step.name} failed with ${step.failureException ?? "an exception"}. The run has crashed: Trax records the failure and the manifest will retry it.`,
      );
  } else if (step.kind === "ROUTE") {
    say(key, t, "info", `${label} # ROUTE took the ${step.answer} track of ${step.questionKey ?? step.name}`);
  } else if (step.replayed) {
    say(
      key,
      t,
      "replay",
      `${label} # MODEL not asked: ${step.questionKey} = ${step.answer} replayed from the failed attempt, because the state hashes the same.`,
    );
  } else {
    const confidence = step.confidence != null ? ` (confidence ${step.confidence.toFixed(2)})` : "";
    say(
      key,
      t,
      "model",
      `${label} # MODEL asked: ${step.questionKey} = ${step.answer}${confidence}. Trax recorded the answer before acting on it.`,
    );
  }
}

function describeJournal(journal: Journal): string {
  const replayed = journal.decisions.filter((d) => d.replayed).length;
  const refused = journal.decisions.filter((d) => d.replayRefused).length;
  const asked = journal.decisions.length - replayed;
  const parts = [`${journal.decisions.length} decision(s) recorded`];
  if (replayed) parts.push(`${replayed} replayed without calling the model`);
  if (asked) parts.push(`${asked} asked of the model`);
  if (refused) parts.push(`${refused} replay(s) refused because the state changed`);
  return parts.join(", ") + ".";
}
