// What the Workflows page draws from a run, worked out from what the host sent: the timeline's grid (one lane per
// attempt, one box per junction, question, track and checkpoint of the train's declared chain) and the model panel's
// cards (one per question, with its words and its answer). Pure functions of the attempts, so they are tested alone.

import { RANK } from "./runReads";
import { branchOf, RESUME_AT, type Attempt, type GraphNode, type Journal, type Phase, type RunGraph, type Step } from "./types";

/** One box of a lane, as the train declares it: a junction, a question, the track a question picked, the checkpoint. */
export interface Column {
  /** The node of operations.runGraph the box stands for, as the steps' nodeId names it. */
  key: string;
  kind: "junction" | "question" | "route" | "checkpoint";
  label: string;
  /** For a question or a route: the question's key. */
  questionKey?: string;
  /** The Parallel branch the node sits in, if any. */
  branch?: string;
}

/** Steps that record nothing when they run, so they take no box. */
const UNRECORDED = new Set(["RESOLVE", "EXTRACT", "SEED"]);

/** The question a Decide node or a routing node asks, from its id: `Decide<ChoiceDecision<Problem>>#0` is Problem. */
export function questionOf(nodeId: string): string {
  const last = nodeId.split("/").pop() ?? nodeId;
  return last.replace(/#\d+$/, "").match(/<(\w+)>+$/)?.[1] ?? last;
}

/**
 * The boxes of the train's chain, from a run graph, in the order the chain declares them. A Parallel step's branches
 * are laid side by side in the row, each branch's nodes in turn; a routing step is one box, filled by whichever track
 * the run took.
 */
export function columnsOf(nodes: GraphNode[], branch?: string): Column[] {
  const columns: Column[] = [];
  for (const node of nodes) {
    if (UNRECORDED.has(node.kind)) continue;
    const base = branch ? { branch } : {};
    if (node.kind === "CHECKPOINT") columns.push({ key: node.id, kind: "checkpoint", label: "Checkpoint", ...base });
    else if (node.kind === "DECIDE") {
      const questionKey = questionOf(node.id);
      columns.push({ key: node.id, kind: "question", label: `${questionKey}?`, questionKey, ...base });
    } else if (node.kind === "PARALLEL") for (const track of node.tracks) columns.push(...columnsOf(track.nodes, track.name));
    else if (node.tracks.length > 0) {
      const questionKey = questionOf(node.id);
      columns.push({ key: node.id, kind: "route", label: `${questionKey} track`, questionKey, ...base });
    } else columns.push({ key: node.id, kind: "junction", label: node.junction ?? questionOf(node.id), ...base });
  }
  return columns;
}

/** Every node of a graph at any depth. */
function flatten(nodes: GraphNode[] | undefined): GraphNode[] {
  return (nodes ?? []).flatMap((n) => [n, ...n.tracks.flatMap((t) => flatten(t.nodes))]);
}

/**
 * The question an attempt is waiting on the model for, read from its run graph: every node before it ended, nothing
 * is running, and the next node is a Decide the attempt has not answered yet. Null when the next thing is not a
 * question, or the graph says nothing yet.
 */
export function pendingQuestion(graph: RunGraph | null | undefined, steps: Step[]): string | null {
  if (!graph?.hasGraph) return null;
  const answered = new Set(steps.filter((s) => isQuestion(s)).map((s) => s.nodeId));
  const walk = (nodes: GraphNode[]): string | null | false => {
    for (const node of nodes) {
      if (UNRECORDED.has(node.kind) || node.kind === "CHECKPOINT") continue;
      if (node.state === "SKIPPED") continue;
      // Branches run side by side: any of them may be waiting on a question while the others run.
      if (node.kind === "PARALLEL" && node.state !== "COMPLETED" && node.state !== "RESTORED") {
        for (const branch of node.tracks) {
          const inside = walk(branch.nodes);
          if (typeof inside === "string") return inside;
        }
        return false;
      }
      if (node.state === "IN_PROGRESS" || node.state === "FAILED" || node.state === "CANCELLED" || node.state === "INTERRUPTED")
        return false;
      if (node.state === "NOT_REACHED") return node.kind === "DECIDE" && !answered.has(node.id) ? node.id : false;
      const taken = node.tracks.find((t) => t.taken);
      if (taken) {
        const inside = walk(taken.nodes);
        if (inside !== null) return inside;
      }
    }
    return null;
  };
  const found = walk(graph.nodes);
  return typeof found === "string" ? found : null;
}

export const isQuestion = (step: Step) => step.kind === "CHOICE" || step.kind === "SCORE" || step.kind === "YES_NO";

/** How a box looks. */
export type SlotTone = "idle" | "running" | "done" | "failed" | "replayed" | "asked" | "saved" | "restored";

export interface LaneSlot {
  key: string;
  label: string;
  /** How far the box is filled, 0 to 1. */
  fill: number;
  tone: SlotTone;
  /** Short words at the box's right edge, such as a question's answer. */
  note?: string;
  title?: string;
  /** A box the attempt never reached, once it is over. */
  dimmed: boolean;
}

export interface Lane {
  id: number;
  label: string;
  status: { text: string; tone: "running" | "done" | "failed" } | null;
  slots: LaneSlot[];
}

/** The fill of a running box: the time it has run, on a curve that nears the end without reaching it. */
export const runningFill = (ms: number) => Math.min(0.92, 1 - Math.exp(-Math.max(0, ms) / 900));

/** An answer as a box shows it: a probability or score to two places, a choice as it is. */
export const shortAnswer = (answer: string | null | undefined) =>
  answer != null && /^-?\d+\.\d{3,}$/.test(answer) ? Number(answer).toFixed(2) : (answer ?? "");

/** The label the timeline and the model panel give an attempt. */
export function labelOf(attempts: Attempt[], attempt: Attempt): string {
  if (attempt.origin === "requeue") return "requeue";
  if (attempt.origin === "resume") return "resume";
  return `attempt ${attempts.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id) + 1}`;
}

const over = (a: Attempt) => a.trainState === "COMPLETED" || a.trainState === "FAILED" || a.trainState === "CANCELLED";

/**
 * The timeline: one lane per attempt, every lane with the same boxes in the same columns. Before a run there is one
 * empty lane, so the grid is drawn in full from the start. `now` is the page's clock in epoch milliseconds.
 */
export function lanesOf(attempts: Attempt[], columns: Column[], now: number): Lane[] {
  if (attempts.length === 0)
    return [{ id: 0, label: "attempt 1", status: null, slots: columns.map((c) => idle(c, false)) }];
  const checkpointAt = columns.findIndex((c) => c.kind === "checkpoint");
  return attempts.map((attempt) => {
    const steps = Object.values(attempt.steps).sort((a, b) => a.position - b.position);
    const nodes = flatten(attempt.graph?.nodes);
    const node = (id: string) => nodes.find((n) => n.id === id);
    const ended = over(attempt);
    const pending = ended ? null : pendingQuestion(attempt.graph, steps);
    // An attempt that started from the checkpoint ran nothing before it: its graph marks those steps restored, or,
    // before the graph is read, every step it holds comes after the checkpoint.
    const resumed =
      checkpointAt > 0 &&
      (nodes.some((n) => n.state === "RESTORED") ||
        (steps.length > 0 && steps.every((s) => columns.findIndex((c) => owns(c, s)) > checkpointAt)));

    const slots = columns.map((column, index): LaneSlot => {
      if (resumed && index < checkpointAt)
        return { key: column.key, label: labelFor(column, steps, attempt), fill: 1, tone: "restored", dimmed: false, title: `${column.label} did not run again: the checkpoint held its result` };
      if (column.kind === "checkpoint") {
        if (resumed) return { key: column.key, label: column.label, fill: 1, tone: "restored", note: "restored", dimmed: false, title: "The attempt started here, from the stored outcome" };
        const reachedAfter = steps.some((s) => columns.findIndex((c) => owns(c, s)) > index);
        return reachedAfter || node(column.key)?.checkpointed
          ? { key: column.key, label: column.label, fill: 1, tone: "saved", note: "saved", dimmed: false, title: "The outcome before it was stored" }
          : idle(column, ended);
      }
      if (column.kind === "question") {
        const step = steps.find((s) => isQuestion(s) && owns(column, s));
        if (!step)
          return pending === column.key
            ? { key: column.key, label: column.label, fill: runningFill(now - lastEnd(steps, attempt)), tone: "running", note: "asking", dimmed: false }
            : idle(column, ended);
        const route = steps.find((s) => s.kind === "ROUTE" && s.questionKey === step.questionKey && sameBranch(s, step));
        const answer = step.kind === "YES_NO" && route?.answer ? route.answer : shortAnswer(step.answer);
        return {
          key: column.key,
          label: column.label,
          fill: 1,
          tone: step.replayed ? "replayed" : "asked",
          note: step.answerWithheld ? "withheld" : step.replayed ? `${answer}, replayed` : answer,
          title: `${step.questionKey} = ${shortAnswer(step.answer)} ${step.replayed ? "(replayed: model not asked)" : "(model asked)"}`,
          dimmed: false,
        };
      }
      // A junction, or the steps on the track a routing step took.
      const mine = steps.filter((s) => s.kind === "JUNCTION" && owns(column, s));
      const step = mine.find((s) => s.state === "IN_PROGRESS") ?? mine[mine.length - 1];
      const label = labelFor(column, steps, attempt);
      if (!step) return { ...idle(column, ended), label };
      const name = step.nameWithheld ? "(withheld)" : step.name;
      if (step.state === "IN_PROGRESS")
        return { key: column.key, label, fill: runningFill(now - Date.parse(step.startedAt)), tone: "running", dimmed: false };
      const failed = step.state === "FAILED" || step.state === "CANCELLED";
      return {
        key: column.key,
        label,
        fill: 1,
        tone: failed ? "failed" : "done",
        title: failed ? `${name} failed with ${step.failureException ?? "an exception"}` : `${name}: ${Math.round(step.durationMs ?? 0)} ms`,
        dimmed: false,
      };
    });

    const manifestIndex = attempts.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id);
    // An attempt that finished what an earlier one crashed in: the manifest's retry, or a resume.
    const recovered = attempt.origin === "resume" || (attempt.origin === "manifest" && manifestIndex > 0);
    const status: Lane["status"] =
      attempt.trainState === "COMPLETED"
        ? { text: recovered ? "Recovered" : "Completed", tone: "done" }
        : attempt.trainState === "FAILED"
          ? { text: "Crashed", tone: "failed" }
          : attempt.trainState === "CANCELLED"
            ? { text: "Cancelled", tone: "failed" }
            : { text: "Running", tone: "running" };
    return { id: attempt.id, label: labelOf(attempts, attempt), status, slots };
  });
}

function idle(column: Column, ended: boolean): LaneSlot {
  return { key: column.key, label: column.label, fill: 0, tone: "idle", dimmed: ended, title: column.branch ? `${column.branch} branch: ${column.label}` : undefined };
}

/** Whether a step ran for a box: the box's own node, or a node on a track of the routing step the box stands for. */
function owns(column: Column, step: Step): boolean {
  if (!step.nodeId) return false;
  if (column.kind === "route") return step.kind === "JUNCTION" && step.nodeId.startsWith(`${column.key}/`);
  return step.nodeId === column.key;
}

const sameBranch = (a: Step, b: Step) => branchOf(a.nodeId) === branchOf(b.nodeId);

/** A routing box is named by the junction on the track it took; until then, by the question it routes on. */
function labelFor(column: Column, steps: Step[], attempt: Attempt): string {
  if (column.kind !== "route") return column.label;
  const ran = steps.find((s) => s.kind === "JUNCTION" && owns(column, s));
  if (ran) return ran.nameWithheld ? "(withheld)" : ran.name;
  const taken = flatten(attempt.graph?.nodes).find((n) => n.id === column.key)?.tracks.find((t) => t.taken);
  const first = taken?.nodes.find((n) => n.junction);
  return first?.junction ?? column.label;
}

/** When the last step before now ended, in epoch milliseconds: where a question's wait starts. */
function lastEnd(steps: Step[], attempt: Attempt): number {
  const ends = steps.map((s) => Date.parse(s.endedAt ?? s.startedAt)).filter((t) => Number.isFinite(t));
  return ends.length ? Math.max(...ends) : Date.parse(attempt.startTime);
}

/** A question as Trax put it to the decider: its words, and each option's description. */
export interface AskedQuestion {
  key?: string;
  type?: string;
  instructions?: string;
  options?: { name: string; description?: string }[];
  yes?: string;
  no?: string;
}

/** An answer as the decider gave it, or as it was replayed. */
export interface GivenAnswer {
  type?: string;
  model?: string;
  choice?: string;
  confidence?: number;
  probability?: number;
  score?: number;
}

/** One question a run put to the model, as the model panel shows it. */
export interface ModelCall {
  key: string;
  attempt: string;
  questionKey: string;
  question: AskedQuestion | null;
  state: "waiting" | "answered";
  /** The answer in words: the choice and how sure, or the probability of yes and the track it took. */
  answer?: string;
  model?: string;
  badge?: { text: string; tone: "replay" | "afresh" | "model" };
  /** How long the answer took, from the end of the step before it. */
  ms?: number;
}

const parse = <T,>(json: string | null | undefined): T | null => {
  if (!json) return null;
  try {
    return JSON.parse(json) as T;
  } catch {
    return null;
  }
};

/** Why a question's answer was or was not replayed, from its junction event and the attempt's decision journal. */
export function badgeOf(attempts: Attempt[], attempt: Attempt, step: Step): ModelCall["badge"] {
  if (step.replayed) return { text: "replayed", tone: "replay" };
  const entry = attempt.journal?.decisions.find((d) => d.questionKey === step.questionKey);
  if (entry?.replayRefused) return { text: "asked afresh: state changed", tone: "afresh" };
  if (attempt.journal?.replayAbandoned) return { text: "asked afresh: replay abandoned", tone: "afresh" };
  if (attempts[0]?.id !== attempt.id)
    return attempt.journal && attempt.journal.replayDecisionsOf == null
      ? { text: "asked afresh: on purpose", tone: "afresh" }
      : { text: "asked afresh", tone: "afresh" };
  return { text: "model asked", tone: "model" };
}

/** The answer in words: "Damaged, 91% sure", or "93% yes: the Yes track". */
export function answerText(step: Step, answer: GivenAnswer | null, track: string | null | undefined): string {
  if (step.answerWithheld) return "(withheld)";
  if (answer?.choice) return answer.confidence != null ? `${answer.choice}, ${Math.round(answer.confidence * 100)}% sure` : answer.choice;
  if (answer?.probability != null) {
    const yes = `${Math.round(answer.probability * 100)}% yes`;
    return track ? `${yes}: the ${track} track` : yes;
  }
  if (step.kind === "YES_NO" && step.answer != null && /^\d/.test(step.answer)) {
    const yes = `${Math.round(Number(step.answer) * 100)}% yes`;
    return track ? `${yes}: the ${track} track` : yes;
  }
  return shortAnswer(step.answer);
}

/**
 * Every question the run has put to the model, oldest first, and the one an attempt is waiting on, read from its run
 * graph. A question's words and answer come from the attempt's decision journal, which the page reads as soon as the
 * question's step arrives; a waiting question shows the words an earlier attempt was asked, when there is one.
 * `expectsReplay` names the attempts whose questions are replayed rather than asked, which show no waiting card.
 */
export function modelCallsOf(attempts: Attempt[], expectsReplay: (attempt: Attempt) => boolean = () => false): ModelCall[] {
  const calls: ModelCall[] = [];
  const asked = new Map<string, AskedQuestion>();
  for (const attempt of attempts) {
    const label = labelOf(attempts, attempt);
    const decided: Journal["decisions"] = attempt.journal?.decisions ?? [];
    const steps = Object.values(attempt.steps).sort((a, b) => a.position - b.position);
    let previousEnd = Date.parse(attempt.startTime);
    const seen = new Map<string, number>();
    for (const step of steps) {
      if (!isQuestion(step)) {
        if (step.kind === "JUNCTION" && RANK[step.state] > 0) previousEnd = Date.parse(step.endedAt ?? step.startedAt);
        continue;
      }
      const key = step.questionKey ?? step.name;
      const occurrence = seen.get(key) ?? 0;
      seen.set(key, occurrence + 1);
      const entry = decided.filter((d) => d.questionKey === key)[occurrence];
      const question = parse<AskedQuestion>(entry?.question);
      if (question) asked.set(key, question);
      const route = steps.find((s) => s.kind === "ROUTE" && s.questionKey === step.questionKey && sameBranch(s, step) && s.position > step.position);
      const answer = parse<GivenAnswer>(entry?.answer);
      const started = Date.parse(step.startedAt);
      calls.push({
        key: `${attempt.id}:${step.position}`,
        attempt: label,
        questionKey: key,
        question: question ?? asked.get(key) ?? null,
        state: "answered",
        answer: answerText(step, answer, route?.answer),
        model: entry?.model ?? answer?.model ?? undefined,
        badge: badgeOf(attempts, attempt, step),
        ms: Number.isFinite(started) && Number.isFinite(previousEnd) ? Math.max(0, started - previousEnd) : undefined,
      });
      if (Number.isFinite(started)) previousEnd = started;
    }
    if (over(attempt) || expectsReplay(attempt)) continue;
    const pending = pendingQuestion(attempt.graph, steps);
    if (pending) {
      const key = questionOf(pending);
      calls.push({ key: `${attempt.id}:${pending}`, attempt: label, questionKey: key, question: asked.get(key) ?? null, state: "waiting" });
    }
  }
  return calls;
}

/**
 * The attempt "Resume from NotifyCustomer" resumes: once every retry failed (the manifest is dead-lettered), the
 * latest failed attempt whose run graph says it can resume there, from the checkpoint it stored. While the run is
 * alive its own retry already resumes from the checkpoint, and a run is resumed to completion only once, so the
 * page offers it at no other time.
 */
export function resumableAttempt(attempts: Attempt[], phase: Phase): Attempt | null {
  if (phase !== "dead") return null;
  const canResume = (nodes: GraphNode[] | undefined): boolean =>
    (nodes ?? []).some((n) => (n.id === RESUME_AT && n.canResume) || n.tracks.some((t) => canResume(t.nodes)));
  return [...attempts].reverse().find((a) => a.trainState === "FAILED" && canResume(a.graph?.nodes)) ?? null;
}
