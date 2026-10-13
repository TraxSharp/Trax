// Plays back the Recovery sample's recorded refund runs (src/data/recovery-recordings.json, written by Trax.Samples'
// scripts/recordings). A recording is what the sample's own page received from the host, with the time each
// thing arrived: the manifest's attempts, every junction event, each attempt's decision journal. Applying the
// events in order rebuilds what that page showed, and narrates it the same way.
//
// A crashing run forks during its backoff (ask afresh, or change the data) and every run forks once it is over
// (ask afresh again, as a requeue). Each fork was recorded as a run of its own, marked where it forked, and the
// recordings number their executions the same way, so switching to another at the fork changes nothing on screen.

export type Scenario = "REFUND";
/** Where the run crashes: nowhere, while it pays (before the checkpoint), or while it emails (after it). */
export type CrashAt = "none" | "paying" | "emailing";
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

/** A question as Trax put it to the decider: its words, and each option's description. */
export interface AskedQuestion {
  key: string;
  type: string;
  instructions: string;
  options?: { name: string; description?: string }[];
  yes?: string;
  no?: string;
}

/** An answer as the decider gave it, or as it was replayed. */
export interface GivenAnswer {
  type: string;
  model?: string;
  choice?: string;
  confidence?: number;
  probability?: number;
}

export interface Journal {
  replayDecisionsOf?: number;
  replayAbandoned?: boolean;
  decisions: {
    questionKey: string;
    replayed?: boolean;
    replayRefused?: string;
    model?: string;
    decider?: string;
    question?: AskedQuestion;
    answer?: GivenAnswer;
  }[];
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
  /** Added by withCheckpointPauses: the attempt stored its checkpoint here. No junction event reports one. */
  | { type: "checkpoint"; attemptId: number }
  | { type: "action"; action: "askAfresh" | "changeData" | "requeue"; change?: string; message?: string; sourceId?: number }
  | { type: "end" }
);

export interface Recording {
  key: string;
  scenario: Scenario;
  orderId?: string;
  crashOnce?: boolean;
  crashWhile?: "PAYING" | "EMAILING";
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
  /** When the attempt stored its checkpoint, in recording time. */
  checkpointAt?: number;
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

/** The recording for an order, where it crashes, and the fork taken in the backoff of a crash while paying. */
export function keyOf(orderId: string, crash: CrashAt, fork: Fork = "none"): string {
  const base = `refund-${orderId}`;
  return crash === "none" ? `${base}-clean` : crash === "emailing" ? `${base}-email` : `${base}-pay-${fork}`;
}

/** Whether the run's crash comes after its checkpoint, so its retry starts there and asks nothing. */
export const pastCheckpoint = (recording: Recording | undefined) => recording?.crashWhile === "EMAILING";

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
  if (phaseOf(playback) !== "breaks" || playback.forkTaken !== "none" || pastCheckpoint(playback.recording)) return null;
  const from = playback.recording;
  const target = recordings.find(
    (r) => r.orderId === from.orderId && r.crashWhile === from.crashWhile && r.crashOnce && r.fork === fork,
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
        event.run.armedCrash === "REFUND_EMAIL"
          ? ", with a crash armed in the email to the customer, after the checkpoint, on the first attempt"
          : event.run.armedCrash === "REFUND_TRACK"
            ? ", with a crash armed in the payment step, on the first attempt"
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
          p.run?.armedCrash === "REFUND_EMAIL"
            ? `${labelOf(p, a)} # FAILED. The scheduler retries after its backoff, starting from the RefundOutcome checkpoint: only the email is sent again.`
            : `${labelOf(p, a)} # FAILED. The scheduler retries after its backoff (a few seconds here), naming execution ${a.id} as the run to replay.`,
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
    case "checkpoint": {
      const a = attempt(event.attemptId);
      if (!a) return;
      a.checkpointAt = event.t;
      say(`${a.id}:checkpoint`, event.t, "replay", `${labelOf(p, a)} # CHECKPOINT stored the RefundOutcome: a retry starts here.`);
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

/** One box of the timeline: a junction or a question of one attempt, filled as far as it got. */
export interface LaneSlot {
  key: string;
  label: string;
  fill: number;
  tone: "idle" | "running" | "done" | "failed" | "replayed" | "asked" | "saved" | "restored";
  /** Short words shown at the box's right edge, such as a question's answer. */
  note?: string;
  title?: string;
  /** A box this attempt never reached, once it is over. */
  dimmed: boolean;
}

export interface TrackLane {
  id: number;
  label: string;
  status: { text: string; tone: "idle" | "running" | "done" | "failed" } | null;
  slots: LaneSlot[];
}

/** The train's checkpoint, which no junction event reports: the timeline draws it before this step. */
export const CHECKPOINT = { label: "Checkpoint", before: "NotifyCustomer" };

/** How long, in recording time, the player lets the checkpoint's box fill before the next step starts. */
export const CHECKPOINT_PAUSE_MS = 700;

/**
 * Marks where each attempt stored its checkpoint, and holds the playback there for CHECKPOINT_PAUSE_MS so the
 * moment can be seen: the real write takes a few milliseconds. An attempt stores it as it starts the step after it,
 * unless the attempt started from the checkpoint and ran nothing before it. Every later event, and every time a
 * later event carries, moves back by the pause.
 */
export function withCheckpointPauses(recording: Recording): Recording {
  const ran = new Map<number, Set<string>>();
  const out: ReplayEvent[] = [];
  // Where each pause starts, in the recording's own time: a time carried by an event moves back by every pause
  // at or before it.
  const pauses: number[] = [];
  let shift = 0;
  const later = (t: number | undefined) => (t == null ? t : t + CHECKPOINT_PAUSE_MS * pauses.filter((p) => p <= t).length);
  for (const event of recording.events) {
    if (event.type === "step" && event.step.kind === "JUNCTION") {
      const names = ran.get(event.attemptId) ?? new Set<string>();
      ran.set(event.attemptId, names);
      if (event.step.name === CHECKPOINT.before && !names.has(CHECKPOINT.before) && names.size > 0) {
        out.push({ t: event.t + shift, type: "checkpoint", attemptId: event.attemptId });
        pauses.push(event.t);
        shift += CHECKPOINT_PAUSE_MS;
      }
      names.add(event.step.name);
    }
    if (shift === 0) {
      out.push(event);
      continue;
    }
    const moved = { ...event, t: event.t + shift } as ReplayEvent;
    if (moved.type === "step") moved.step = { ...moved.step, start: later(moved.step.start), end: later(moved.step.end) };
    if (moved.type === "attempt" || moved.type === "row") moved.row = { ...moved.row, start: later(moved.row.start), end: later(moved.row.end) };
    out.push(moved);
  }
  return { ...recording, events: out };
}

/**
 * The timeline as a fixed grid, drawn in full before anything plays: one lane per attempt the recording holds,
 * one box per junction or question, each filling as the playback reaches it. A route takes no box: its track is
 * written on the question it answers. The checkpoint has a box of its own: saved once the step before it
 * completes, and restored on an attempt that started from it, whose earlier boxes are restored with it.
 */
export function trackOf(recording: Recording | undefined, playback: Playback | null, elapsed: number): TrackLane[] {
  if (!recording) return [];
  const showRequeue = playback?.requeued ?? false;
  const planned = new Map<number, { origin: "manifest" | "requeue"; steps: Map<number, Step> }>();
  for (const e of recording.events) {
    if (e.type === "attempt" && (e.origin === "manifest" || showRequeue)) planned.set(e.row.id, { origin: e.origin, steps: new Map() });
    if (e.type === "step") planned.get(e.attemptId)?.steps.set(e.step.position, e.step);
  }
  // The columns: every junction and question, by name, in the order the longest attempt took them, with the
  // checkpoint before the step it comes before.
  const columns: string[] = [];
  const longest = [...planned.values()].sort((x, y) => y.steps.size - x.steps.size)[0];
  for (const s of [...(longest?.steps.values() ?? [])].sort((x, y) => x.position - y.position)) {
    if (s.kind === "ROUTE") continue;
    if (slotName(s) === CHECKPOINT.before) columns.push(CHECKPOINT.label);
    if (!columns.includes(slotName(s))) columns.push(slotName(s));
  }
  const checkpointAt = columns.indexOf(CHECKPOINT.label);

  let manifestIndex = 0;
  return [...planned.entries()].map(([id, plan]) => {
    const live = playback?.attempts.find((a) => a.id === id);
    const label = plan.origin === "requeue" ? "requeue" : `attempt ${++manifestIndex}`;
    const over = live != null && (live.trainState === "COMPLETED" || live.trainState === "FAILED");
    const byName = new Map([...plan.steps.values()].filter((s) => s.kind !== "ROUTE").map((s) => [slotName(s), s]));
    // An attempt that started from the checkpoint holds no step before it.
    const resumed = checkpointAt > 0 && byName.size > 0 && [...byName.keys()].every((n) => columns.indexOf(n) > checkpointAt);
    const liveStep = (name: string) => {
      const final = byName.get(name);
      return final && live ? live.steps[final.position] : undefined;
    };

    const slots = columns.map((name, column): LaneSlot => {
      if (name === CHECKPOINT.label) {
        if (resumed && live) return { key: name, label: name, fill: 1, tone: "restored", note: "restored", title: "The retry started here, from the stored RefundOutcome", dimmed: false };
        if (live?.checkpointAt != null) {
          const fill = Math.min(1, Math.max(0, (elapsed - live.checkpointAt) / CHECKPOINT_PAUSE_MS));
          return fill < 1
            ? { key: name, label: name, fill, tone: "saved", note: "saving", dimmed: false }
            : { key: name, label: name, fill: 1, tone: "saved", note: "saved", title: "The RefundOutcome was stored", dimmed: false };
        }
        const before = liveStep(columns[column - 1]);
        const after = liveStep(columns[column + 1]);
        const saved = (before?.state === "COMPLETED") || after != null;
        return saved
          ? { key: name, label: name, fill: 1, tone: "saved", note: "saved", title: "The RefundOutcome was stored", dimmed: false }
          : { key: name, label: name, fill: 0, tone: "idle", dimmed: over || (playback?.finished ?? false) };
      }
      if (resumed && live && column < checkpointAt)
        return { key: name, label: name, fill: 1, tone: "restored", title: `${name} did not run again: the checkpoint held its result`, dimmed: false };
      const final = byName.get(name);
      const now = liveStep(name);
      if (!final || !now) return { key: name, label: name, fill: 0, tone: "idle", dimmed: over || (playback?.finished ?? false) };
      if (now.kind !== "JUNCTION") {
        const route = Object.values(live!.steps).find((s) => s.kind === "ROUTE" && s.questionKey === now.questionKey);
        const answer = route?.answer ?? now.answer;
        return {
          key: name,
          label: name,
          fill: 1,
          tone: now.replayed ? "replayed" : "asked",
          note: now.replayed ? `${answer}, replayed` : answer,
          title: `${now.questionKey} = ${now.answer}${now.replayed ? " (replayed: model not asked)" : " (model asked)"}`,
          dimmed: false,
        };
      }
      if (now.state === "IN_PROGRESS") {
        const length = (final.end ?? elapsed) - (now.start ?? elapsed);
        const fill = length > 0 ? Math.min(1, Math.max(0, (elapsed - (now.start ?? elapsed)) / length)) : 0.5;
        return { key: name, label: name, fill, tone: "running", dimmed: false };
      }
      return {
        key: name,
        label: name,
        fill: 1,
        tone: now.state === "FAILED" ? "failed" : "done",
        title: now.state === "FAILED" ? `${name} failed with ${now.failureException ?? "an exception"}` : `${name}: ${now.durationMs ?? 0} ms`,
        dimmed: false,
      };
    });
    const status: TrackLane["status"] = !live
      ? null
      : live.trainState === "COMPLETED"
        ? { text: manifestIndex > 1 && plan.origin === "manifest" ? "Recovered" : "Completed", tone: "done" }
        : live.trainState === "FAILED"
          ? { text: "Crashed", tone: "failed" }
          : { text: "Running", tone: "running" };
    return { id, label, status, slots };
  });
}

function slotName(step: Step): string {
  return step.kind === "JUNCTION" ? step.name : `${step.questionKey ?? step.name}?`;
}

/** One question a run put to the model, as the model panel shows it. */
export interface ModelCall {
  key: string;
  attempt: string;
  questionKey: string;
  question: AskedQuestion | null;
  /** Waiting for the answer, or answered. */
  state: "waiting" | "answered";
  /** The answer in words: the choice, or the probability of yes and the track it took. */
  answer?: string;
  model?: string;
  badge?: { text: string; tone: string };
  /** How long the answer took, from the step before it. */
  ms?: number;
}

/**
 * Every question the run has put to the model so far, oldest first, and the one it is waiting on. The question's
 * words and the answer come from the attempt's decision journal, which the recording holds from the start.
 */
export function modelCallsOf(playback: Playback | null): ModelCall[] {
  if (!playback) return [];
  const journals = new Map<number, Journal>();
  const planned = new Map<number, Step[]>();
  for (const e of playback.recording.events) {
    if (e.type === "journal") journals.set(e.attemptId, e.journal);
    if (e.type === "step") {
      const steps = planned.get(e.attemptId) ?? [];
      const at = steps.findIndex((s) => s.position === e.step.position);
      if (at >= 0) steps[at] = e.step;
      else steps.push(e.step);
      planned.set(e.attemptId, steps);
    }
  }
  const calls: ModelCall[] = [];
  for (const attempt of playback.attempts) {
    const label = labelOf(playback, attempt).replace(/[[\]]/g, "");
    const decided = journals.get(attempt.id)?.decisions ?? [];
    const steps = Object.values(attempt.steps).sort((a, b) => a.position - b.position);
    let previousEnd = attempt.start ?? 0;
    for (const step of steps) {
      if (step.kind === "JUNCTION" || step.kind === "ROUTE") {
        previousEnd = step.end ?? step.start ?? previousEnd;
        continue;
      }
      const entry = decided.find((d) => d.questionKey === step.questionKey);
      const route = steps.find((s) => s.kind === "ROUTE" && s.questionKey === step.questionKey);
      calls.push({
        key: `${attempt.id}:${step.position}`,
        attempt: label,
        questionKey: step.questionKey ?? step.name,
        question: entry?.question ?? null,
        state: "answered",
        answer: answerText(step, entry?.answer, route?.answer),
        model: entry?.model ?? entry?.answer?.model,
        badge: badgeOf(playback, attempt, step) ?? undefined,
        ms: step.start != null ? Math.max(0, step.start - previousEnd) : undefined,
      });
      previousEnd = step.start ?? previousEnd;
    }
    // The question the attempt is waiting on: the next step it took, when that is a question not yet answered.
    const running = attempt.trainState !== "COMPLETED" && attempt.trainState !== "FAILED";
    const last = steps[steps.length - 1];
    if (running && (!last || last.state !== "IN_PROGRESS")) {
      const next = (planned.get(attempt.id) ?? []).filter((s) => !attempt.steps[s.position]).sort((a, b) => a.position - b.position)[0];
      if (next && next.kind !== "JUNCTION" && next.kind !== "ROUTE") {
        const entry = decided.find((d) => d.questionKey === next.questionKey);
        calls.push({
          key: `${attempt.id}:${next.position}`,
          attempt: label,
          questionKey: next.questionKey ?? next.name,
          question: entry?.question ?? null,
          state: "waiting",
        });
      }
    }
  }
  return calls;
}

function answerText(step: Step, answer: GivenAnswer | undefined, track: string | undefined): string {
  if (answer?.choice) return answer.confidence != null ? `${answer.choice}, ${Math.round(answer.confidence * 100)}% sure` : answer.choice;
  if (answer?.probability != null) {
    const yes = `${Math.round(answer.probability * 100)}% yes`;
    return track ? `${yes}: the ${track} track` : yes;
  }
  return step.answer ?? "";
}
