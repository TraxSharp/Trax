// Plays back the SignalRBroadcaster sample (src/data/signalr-recordings.json, written by Trax.Samples'
// scripts/recordings/signalr-broadcaster.mjs). The recording is what the sample's page sent and received: a hub
// connection refused without signing in, the demo sign-in, the hub connection, each ping button pressed six times
// and every TrainEvent the hub pushed for those runs, with the time each arrived. Pressing a control here schedules
// the recorded messages at their recorded offsets from the press, and applying them rebuilds what that page showed.

export type Outcome = "Succeed" | "FailForClients" | "FailUnexpectedly";

/** The sample's LiveTrainEvent, as the hub sends it (camelCase). */
export interface TrainEvent {
  externalId: string;
  trainName: string;
  eventType: "Started" | "Completed" | "Failed";
  timestamp: string;
  failureReason: string | null;
}

export interface RecordedRun {
  outcome: Outcome;
  body: { outcome: Outcome };
  /** The 202 to POST /pings, milliseconds after the press. */
  response: { t: number; status: number; statusText: string; body: string };
  /** Each event: when it arrived after the press, and its server timestamp relative to the run's first event. */
  events: { t: number; serverMs: number; evt: TrainEvent }[];
}

export interface SignalRRecording {
  sources: Record<SourceKey, { file: string; code: string }>;
  probe: { status: number; statusText: string; ms: number };
  signIn: { status: number; location: string; me: { status: number; body: { name: string } }; ms: number; signedInMs: number };
  connect: { connectionId: string; ms: number };
  signOut: { status: number; location: string; me: number; ms: number };
  runs: RecordedRun[];
}

export type SourceKey = "program" | "projection" | "junction";

/** What the code panel points at: a file of the sample, and the line holding `needle`. */
export interface Focus {
  source: SourceKey;
  needle: string;
}

export interface Traffic {
  key: string;
  /** Wall-clock milliseconds when it happened, for the time column. */
  at: number;
  direction: "out" | "in" | "info";
  channel: "http" | "hub";
  title: string;
  note?: string;
  detail?: unknown;
}

export interface RunCard {
  externalId: string;
  trainName: string;
  status: "Running" | "Completed" | "Failed";
  /** Each event shown on the card: its type, its wall-clock time, and the gap from the first event. */
  steps: { eventType: TrainEvent["eventType"]; at: number; gapMs: number | null }[];
  reason: { text: string; withheld: boolean } | null;
}

export interface HubState {
  signedIn: boolean;
  who: string | null;
  hub: "disconnected" | "connecting" | "connected";
  connectionId: string | null;
  probe: string | null;
  traffic: Traffic[];
  /** Newest first, as the page lists them. */
  runs: RunCard[];
  focus: Focus | null;
}

/** One recorded message to apply `at` milliseconds after the press that scheduled it. */
export type Scheduled = { at: number } & (
  | { kind: "traffic"; traffic: Omit<Traffic, "at" | "key">; focus?: Focus }
  | { kind: "probeResult"; text: string }
  | { kind: "signedIn"; who: string }
  | { kind: "connected"; connectionId: string }
  | { kind: "event"; evt: TrainEvent; serverMs: number; focus: Focus }
  | { kind: "signedOut" }
);

export const MASKED = "The run failed. The reason is in the server log.";
const NEGOTIATE = "/hubs/trax-events/negotiate?negotiateVersion=1";

export function initial(): HubState {
  return { signedIn: false, who: null, hub: "disconnected", connectionId: null, probe: null, traffic: [], runs: [], focus: null };
}

/** Try to connect without signing in: the negotiate request, and the 401 that refuses it. */
export function probe(rec: SignalRRecording): Scheduled[] {
  const ok = rec.probe.status === 401;
  return [
    {
      at: 0,
      kind: "traffic",
      traffic: { direction: "out", channel: "http", title: "POST negotiate", note: "Asks the hub for a connection, with no sign-in cookie." },
      focus: { source: "program", needle: "app.MapTraxTrainEventHub(" },
    },
    {
      at: rec.probe.ms,
      kind: "traffic",
      traffic: {
        direction: "in",
        channel: "http",
        title: `${rec.probe.status} ${rec.probe.statusText}`,
        note: ok ? "The hub refused before any connection opened." : undefined,
      },
    },
    {
      at: rec.probe.ms,
      kind: "probeResult",
      text: `POST ${NEGOTIATE}  →  ${rec.probe.status} ${ok ? "Unauthorized: no connection was opened." : rec.probe.statusText}`,
    },
  ];
}

/** Sign in as the demo operator, then connect to the hub with the cookie, as the page does after it reloads. */
export function signIn(rec: SignalRRecording): Scheduled[] {
  const s = rec.signIn;
  const connectAt = s.ms + 20;
  return [
    {
      at: 0,
      kind: "traffic",
      traffic: { direction: "out", channel: "http", title: "POST /demo/sign-in", note: "The demo sign-in form. Development only." },
      focus: { source: "program", needle: '"/demo/sign-in"' },
    },
    {
      at: s.signedInMs,
      kind: "traffic",
      traffic: { direction: "in", channel: "http", title: `${s.status} Found`, note: `Sets the sign-in cookie and redirects to ${s.location}.` },
    },
    {
      at: s.ms,
      kind: "traffic",
      traffic: { direction: "in", channel: "http", title: `GET /me: ${s.me.status}`, detail: s.me.body },
      focus: { source: "program", needle: '"/me"' },
    },
    { at: s.ms, kind: "signedIn", who: s.me.body.name },
    {
      at: connectAt,
      kind: "traffic",
      traffic: {
        direction: "out",
        channel: "hub",
        title: "Connect /hubs/trax-events",
        note: "Negotiates, then opens the connection. The sign-in cookie goes with both, so the page sends no token.",
      },
      focus: { source: "program", needle: "app.MapTraxTrainEventHub(" },
    },
    {
      at: connectAt + rec.connect.ms,
      kind: "traffic",
      traffic: {
        direction: "in",
        channel: "hub",
        title: "Connected",
        note: `Connection ${rec.connect.connectionId}. Every TrainEvent the sink sends now reaches this page.`,
      },
    },
    { at: connectAt + rec.connect.ms, kind: "connected", connectionId: rec.connect.connectionId },
  ];
}

/** Sign out: the form posts, the cookie is cleared and the page goes back to the sign-in gate. */
export function signOut(rec: SignalRRecording): Scheduled[] {
  return [
    {
      at: 0,
      kind: "traffic",
      traffic: { direction: "out", channel: "http", title: "POST /sign-out" },
      focus: { source: "program", needle: '"/sign-out"' },
    },
    { at: rec.signOut.ms, kind: "signedOut" },
  ];
}

/** The recorded runs of one button, in the order they were recorded. */
export function runsOf(rec: SignalRRecording, outcome: Outcome): RecordedRun[] {
  return rec.runs.filter((r) => r.outcome === outcome);
}

/** Press a ping button for the `n`th time: the POST, the run's TrainEvents, and the 202, at their recorded times. */
export function ping(rec: SignalRRecording, outcome: Outcome, n: number): Scheduled[] {
  const runs = runsOf(rec, outcome);
  const run = runs[n % runs.length];
  const out: Scheduled[] = [
    {
      at: 0,
      kind: "traffic",
      traffic: { direction: "out", channel: "http", title: "POST /pings", note: "Runs the ping train on the server.", detail: run.body },
      focus: { source: "program", needle: '"/pings"' },
    },
  ];
  for (const e of run.events) {
    const note =
      e.evt.eventType === "Failed"
        ? e.evt.failureReason === MASKED
          ? "Failed with an exception the hub does not repeat: a fixed sentence goes out instead."
          : "Failed with a TrainException: its message is meant to be read, so it goes out."
        : `The ${e.evt.trainName} run ${e.evt.eventType.toLowerCase()}.`;
    out.push({ at: e.t, kind: "traffic", traffic: { direction: "in", channel: "hub", title: `TrainEvent · ${e.evt.eventType}`, note, detail: e.evt } });
    out.push({ at: e.t, kind: "event", evt: e.evt, serverMs: e.serverMs, focus: focusOf(e.evt, outcome) });
  }
  out.push({
    at: run.response.t,
    kind: "traffic",
    traffic: {
      direction: "in",
      channel: "http",
      title: `${run.response.status} ${run.response.statusText}`,
      note: "No result in the response: the run's events come over the hub.",
    },
  });
  return out.sort((a, b) => a.at - b.at);
}

/** The line of the sample that explains an event: the junction's outcome, and for a failure, the projection's rule. */
export function focusOf(evt: TrainEvent, outcome: Outcome): Focus {
  if (evt.eventType === "Started") return { source: "program", needle: ".OnlyForEvents(" };
  if (evt.eventType === "Completed") return { source: "junction", needle: "_ => new PingOutput" };
  return evt.failureReason === MASKED
    ? { source: "projection", needle: ": MaskedReason;" }
    : outcome === "FailForClients"
      ? { source: "projection", needle: "message.FailureException == nameof(TrainException)" }
      : { source: "projection", needle: "private static string ClientReason" };
}

/** Applies one scheduled message. `now` is the wall-clock time it applies at; `seq` keys its traffic entry. */
export function apply(state: HubState, s: Scheduled, now: number, seq: number): HubState {
  const next: HubState = { ...state, traffic: state.traffic, runs: state.runs };
  switch (s.kind) {
    case "traffic":
      next.traffic = [...state.traffic, { ...s.traffic, at: now, key: `t${seq}` }].slice(-200);
      if (s.focus) next.focus = s.focus;
      return next;
    case "probeResult":
      next.probe = s.text;
      return next;
    case "signedIn":
      return { ...next, signedIn: true, who: s.who, hub: "connecting", probe: null };
    case "connected":
      return { ...next, hub: "connected", connectionId: s.connectionId };
    case "signedOut":
      // The page reloads at the sign-in gate: the connection closes and everything it showed goes with it.
      return initial();
    case "event": {
      next.focus = s.focus;
      const evt = s.evt;
      const live = state.runs.find((r) => r.externalId === evt.externalId && r.status === "Running");
      if (!live) {
        // A new run. A recorded run plays again once every run of its button has been shown: drop its old card.
        const card: RunCard = {
          externalId: evt.externalId,
          trainName: evt.trainName,
          status: evt.eventType === "Started" ? "Running" : evt.eventType,
          steps: [{ eventType: evt.eventType, at: now, gapMs: null }],
          reason: reasonOf(evt),
        };
        next.runs = [card, ...state.runs.filter((r) => r.externalId !== evt.externalId)];
        return next;
      }
      // The card's times are the first event's arrival plus the server's own gaps, as recorded.
      const first = live.steps[0];
      const card: RunCard = {
        ...live,
        status: evt.eventType === "Started" ? live.status : evt.eventType,
        steps: [...live.steps, { eventType: evt.eventType, at: first.at + s.serverMs, gapMs: s.serverMs }],
        reason: reasonOf(evt) ?? live.reason,
      };
      next.runs = state.runs.map((r) => (r === live ? card : r));
      return next;
    }
  }
}

function reasonOf(evt: TrainEvent): RunCard["reason"] {
  return evt.eventType === "Failed" ? { text: evt.failureReason ?? "", withheld: evt.failureReason === MASKED } : null;
}

/** The 0-based line of a source holding the focus's needle, or -1. */
export function lineOf(code: string, focus: Focus): number {
  return code.split("\n").findIndex((l) => l.includes(focus.needle));
}
