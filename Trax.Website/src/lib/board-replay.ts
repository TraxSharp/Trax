// Plays back the agents demo (src/data/board-recordings.json): what the Board sample's scripted agents sent from
// their own processes, what the host answered, the check trains it ran, and how each ticket moved on the board, with
// the time each thing arrived. The page steps through it a move at a time; everything it shows after a move is worked
// out from the events up to and including that move, so this file holds no state of its own.
//
// The recording is read defensively: fields it does not know are ignored, and a missing one leaves its part blank.

export interface Problem {
  code: string;
  message: string;
}

export interface BoardEvent {
  t: number;
  move: number;
  kind: "claim" | "package" | "start" | "send" | "answer" | "check" | "ticket";
  agent?: string;
  ticketId?: string;
  say?: string;
  /** What the move means, written by the scenario's script on the move's first event. */
  caption?: string;
  claim?: { lane: string; problem: Problem | null };
  package?: { ticket: string; lane: string; instructions?: string; tools?: string[]; deadline?: string };
  send?: { operation: string; input?: Record<string, unknown> };
  answer?: { state: string | null; revision?: number | null; problem: Problem | null };
  check?: { train: string; status: "running" | "done" | "failed"; message?: string };
  ticket?: { id: string; state: string; note?: string; by?: string };
  line?: string;
}

export interface Scenario {
  key: string;
  title: string;
  caption: string;
  tickets: { id: string; title: string; state: string }[];
  events: BoardEvent[];
}

export interface BoardRecordings {
  recordedAt: string;
  sources: Record<string, { file: string; code: string }>;
  lines: Record<string, number>;
  lanes: { key: string; name: string; states: string[]; worker: string | null }[];
  agents: { name: string; role: string; kind: "agent" | "person" }[];
  scenarios: Scenario[];
}

export type Tone = "ok" | "bad" | "warn" | "info" | "idle";

// One colour per client, in the order the recording lists them: the board, the pods and the panel all use it.
const CLIENT_COLORS = ["#7a9ec2", "#c4a468", "#a093c6", "#5fb3a6", "#6fa86f", "#c68f68"];

export function clientColor(recordings: BoardRecordings, name: string | null | undefined): string {
  const i = recordings.agents.findIndex((a) => a.name === name);
  return i < 0 ? "#3a3a37" : CLIENT_COLORS[i % CLIENT_COLORS.length];
}

/** The states a pod's Assignment strip shows, in order. */
export const ASSIGNMENT_STATES = ["Working", "Checking", "Accepted", "Rejected"] as const;
export type AssignmentState = (typeof ASSIGNMENT_STATES)[number];

export interface TicketView {
  id: string;
  title: string;
  state: string;
  lane: string;
  note: string;
  /** The agent holding the ticket while it works a lane, or null. */
  holder: string | null;
  /** Whether the server has moved the ticket since the scenario began. */
  moved: boolean;
  /** Whether this move touched the ticket. */
  live: boolean;
}

export interface PodView {
  name: string;
  kind: "agent" | "person";
  /** The lane the client's role works. */
  lane: string | null;
  package: string | null;
  assignment: AssignmentState | null;
  say: string | null;
  verdict: string | null;
  badge: string | null;
  tone: Tone;
  current: boolean;
}

export interface View {
  move: number;
  caption: string;
  tickets: TicketView[];
  pods: PodView[];
  /** The line the move's last decision was made on: which source and which line, and how it went. */
  lit: { source: string; line: number; tone: Tone } | null;
}

export function eventsOf(scenario: Scenario | undefined): BoardEvent[] {
  return Array.isArray(scenario?.events) ? scenario.events : [];
}

export function movesOf(scenario: Scenario | undefined): number {
  return eventsOf(scenario).reduce((n, e) => Math.max(n, e.move ?? 0), 0);
}

/** When a move's first event arrived, for pacing a replay that plays itself. */
export function moveStart(scenario: Scenario | undefined, move: number): number {
  return eventsOf(scenario).find((e) => e.move === move)?.t ?? 0;
}

export function laneOf(recordings: BoardRecordings, state: string): { key: string; name: string } | null {
  const lane = recordings.lanes.find((l) => l.states.includes(state));
  return lane ? { key: lane.key, name: lane.name } : null;
}

const laneName = (recordings: BoardRecordings, key: string | undefined) =>
  recordings.lanes.find((l) => l.key === key)?.name ?? key ?? "";

const problemText = (p: Problem) => `${p.code}: ${p.message}`;

function packageText(recordings: BoardRecordings, p: NonNullable<BoardEvent["package"]>): string {
  const parts = [p.ticket, laneName(recordings, p.lane)];
  if (p.tools?.length) parts.push(`tools: ${p.tools.join(", ")}`);
  const lease = durationText(p.deadline);
  if (lease) parts.push(`lease ${lease}`);
  return parts.join(" · ");
}

// The strip's state for a state of the sample's AssignmentMachine: each lane has a Checking state of its own
// (CheckingRepro, CheckingFix, ...), and a close the server accepts ends in Closed.
function assignmentOf(state: string | null | undefined): AssignmentState | null {
  if (!state) return null;
  if (state.startsWith("Checking")) return "Checking";
  if (state === "Closed") return "Accepted";
  return ASSIGNMENT_STATES.find((s) => s === state) ?? null;
}

// A claim is written on the ticket too, as its Claim event; it marks who holds the ticket, and moves nothing.
const isClaim = (e: BoardEvent) => e.kind === "ticket" && e.line === "ticket:event:Claim";

interface MoveSummary {
  agent: string | null;
  say: string | null;
  verdict: string | null;
  badge: string | null;
  tone: Tone;
  caption: string;
}

// What one move came to, from its own events: its last refusal, check or ticket move decides how it is told. The
// script's caption, when the move has one, is what the page shows; the one worked out here stands in for a recording
// without them. `before` is each ticket's state before the move, so a ticket sent back to an earlier lane is told as
// that.
function summarize(recordings: BoardRecordings, events: BoardEvent[], before: Map<string, string>): MoveSummary {
  const summary = told(recordings, events, before);
  const caption = events.find((e) => e.caption)?.caption;
  return caption ? { ...summary, caption } : summary;
}

function told(recordings: BoardRecordings, events: BoardEvent[], before: Map<string, string>): MoveSummary {
  const agent = events.find((e) => e.agent)?.agent ?? null;
  const say = events.find((e) => e.say)?.say ?? null;
  const refusal =
    events.find((e) => e.claim?.problem)?.claim?.problem ?? events.find((e) => e.answer?.problem)?.answer?.problem ?? null;
  const claim = events.find((e) => e.kind === "claim")?.claim;
  const send = events.find((e) => e.kind === "send")?.send;
  const checks = events.filter((e) => e.kind === "check" && e.check && e.check.status !== "running");
  const check = checks[checks.length - 1]?.check;
  const moved = events.filter((e) => e.kind === "ticket" && e.ticket && !isClaim(e)).pop()?.ticket;
  const changed = moved && before.get(moved.id) !== moved.state ? moved : undefined;
  const who = agent ?? "A client";

  if (refusal) {
    const asked = claim ? `asks for work in ${laneName(recordings, claim.lane)}` : `sends ${send?.operation ?? "an event"}`;
    return { agent, say, verdict: problemText(refusal), badge: "refused", tone: "bad", caption: `${who} ${asked}. The server refuses: ${refusal.message}` };
  }
  const to = changed ? laneOf(recordings, changed.state)?.name ?? changed.state : null;
  if (check?.status === "failed") {
    return {
      agent,
      say,
      verdict: `${check.train}: ${check.message ?? "failed"}${to ? ` \u2192 ticket back to ${to}` : ""}`,
      badge: "rejected",
      tone: "warn",
      caption: `${who} sends ${send?.operation ?? "its evidence"}. The server runs ${check.train} itself, and it fails${check.message ? `: ${check.message}` : ""}. ${
        to ? `The server sends the ticket back to ${to}.` : "The ticket stays where it is."
      }`,
    };
  }
  // A check that passes and moves the ticket to an earlier lane found a problem: QA's sweep, say, or a review sent
  // back.
  const laneIndex = (state: string | undefined) => recordings.lanes.findIndex((l) => state !== undefined && l.states.includes(state));
  const backwards = !!changed && laneIndex(changed.state) < laneIndex(before.get(changed.id));
  if (check?.status === "done" && backwards) {
    return {
      agent,
      say,
      verdict: `${check.train}: ${check.message ?? "found a problem"}`,
      badge: "sent back",
      tone: "warn",
      caption: `${who} sends ${send?.operation ?? "its evidence"}. The server runs ${check.train} itself${
        check.message ? `: ${check.message}` : ""
      }. The ticket goes back to ${to}.`,
    };
  }
  if (check?.status === "done") {
    return {
      agent,
      say,
      verdict: `${check.train}: ${check.message ?? "passed"}${to ? ` → ticket to ${to}` : ""}`,
      badge: "accepted",
      tone: "ok",
      caption: `${who} sends ${send?.operation ?? "its evidence"}. The server runs ${check.train} itself, it passes, and the server moves the ticket${to ? ` to ${to}` : ""}.`,
    };
  }
  if (to) {
    return { agent, say, verdict: `→ ticket to ${to}`, badge: "accepted", tone: "ok", caption: `${who} finishes its lane, and the server moves the ticket to ${to}.` };
  }
  if (claim) {
    return { agent, say, verdict: "claimed", badge: "claimed", tone: "info", caption: `${who} claims work in ${laneName(recordings, claim.lane)} and gets its work package.` };
  }
  return { agent, say, verdict: null, badge: null, tone: "idle", caption: `${who} acts.` };
}

export function viewAt(recordings: BoardRecordings, scenario: Scenario | undefined, move: number): View {
  const events = eventsOf(scenario).filter((e) => (e.move ?? 0) <= move);

  const tickets = new Map<string, TicketView>();
  for (const t of scenario?.tickets ?? []) {
    tickets.set(t.id, {
      id: t.id,
      title: t.title,
      state: t.state,
      lane: laneOf(recordings, t.state)?.key ?? "",
      note: "new report",
      holder: null,
      moved: false,
      live: false,
    });
  }

  const roleLane = (name: string) => {
    const role = recordings.agents.find((a) => a.name === name)?.role;
    const byWorker = recordings.lanes.find((l) => l.worker === name);
    return byWorker?.key ?? (role ? recordings.lanes.find((l) => l.key === role)?.key ?? null : null);
  };
  const pods = new Map<string, PodView>();
  for (const a of recordings.agents) {
    pods.set(a.name, {
      name: a.name,
      kind: a.kind,
      lane: roleLane(a.name),
      package: null,
      assignment: null,
      say: null,
      verdict: null,
      badge: null,
      tone: "idle",
      current: false,
    });
  }

  for (const e of events) {
    const pod = e.agent ? pods.get(e.agent) : undefined;
    const ticket = e.ticketId ? tickets.get(e.ticketId) : undefined;
    if (e.kind === "claim" && pod) {
      // A new claim starts the pod over: the package, the machine and the words are this claim's.
      Object.assign(pod, { package: null, assignment: null, verdict: null });
      if (!e.claim?.problem && ticket) ticket.holder = e.agent ?? null;
      if (e.claim?.problem) pod.assignment = null;
    }
    if (e.kind === "package" && pod && e.package) pod.package = packageText(recordings, e.package);
    if (e.kind === "start" && pod) {
      // A start of its own is the pod's first assignment for a lane it did not claim (fix-bot sending an approval).
      if (!pod.package && e.ticketId) pod.package = `${e.ticketId} \u00b7 no claim`;
      pod.assignment = assignmentOf(e.answer?.state) ?? "Working";
    }
    if (e.kind === "answer" && pod && e.answer && !e.answer.problem) pod.assignment = assignmentOf(e.answer.state) ?? pod.assignment;
    if (e.kind === "check" && pod && e.check) {
      pod.assignment = e.check.status === "running" ? "Checking" : e.check.status === "done" ? "Accepted" : "Rejected";
    }
    if (isClaim(e)) continue;
    if (e.kind === "ticket" && e.ticket) {
      const moved = tickets.get(e.ticket.id);
      if (moved) {
        moved.state = e.ticket.state;
        moved.lane = laneOf(recordings, e.ticket.state)?.key ?? moved.lane;
        if (e.ticket.note) moved.note = e.ticket.note;
        moved.holder = null;
        moved.moved = true;
      }
    }
  }

  // Each pod keeps the words and verdict of the last move it made.
  const stateBefore = new Map((scenario?.tickets ?? []).map((t) => [t.id, t.state] as [string, string]));
  let summary: MoveSummary | null = null;
  for (let m = 1; m <= move; m++) {
    const own = events.filter((e) => e.move === m);
    if (!own.length) continue;
    const s = summarize(recordings, own, stateBefore);
    for (const e of own) if (e.kind === "ticket" && e.ticket && !isClaim(e)) stateBefore.set(e.ticket.id, e.ticket.state);
    if (m === move) summary = s;
    const pod = s.agent ? pods.get(s.agent) : undefined;
    if (pod) Object.assign(pod, { say: s.say, verdict: s.verdict, badge: s.badge, tone: s.tone });
  }

  const current = events.filter((e) => e.move === move);
  if (summary?.agent) {
    const pod = pods.get(summary.agent);
    if (pod) pod.current = true;
  }
  for (const e of current) {
    const touched = e.ticketId ?? e.ticket?.id;
    const t = touched ? tickets.get(touched) : undefined;
    if (t) t.live = true;
  }

  const litKey = [...current].reverse().find((e) => e.line)?.line;
  const [source] = litKey?.split(":") ?? [];
  const line = litKey ? recordings.lines[litKey] : undefined;

  return {
    move,
    caption: summary?.caption ?? scenario?.caption ?? "",
    tickets: [...tickets.values()],
    pods: [...pods.values()],
    lit: source && typeof line === "number" && recordings.sources[source] ? { source, line, tone: summary?.tone ?? "ok" } : null,
  };
}

/** "PT10M" as "10m", "PT1H30M" as "1h 30m"; null when there is none or it does not read. */
export function durationText(iso: string | undefined): string | null {
  const m = iso ? /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso) : null;
  if (!m || !(m[1] || m[2] || m[3])) return null;
  return [m[1] && `${m[1]}h`, m[2] && `${m[2]}m`, m[3] && `${m[3]}s`].filter(Boolean).join(" ");
}
