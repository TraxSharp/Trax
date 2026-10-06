// Plays back the StateMachine sample's recordings (src/data/state-machine-recordings.json, written by Trax.Samples'
// scripts/recordings/state-machine.mjs). Each machine was recorded as a graph: a node is a draft's state and
// context, an edge is one of the page's buttons pressed there, with the request the page sent and the server's
// whole response. Pressing a button follows its edge, so every answer shown is one the server gave at that point.

export type MachineName = "turnstile" | "checkout";

export interface Snapshot {
  machine: string;
  version: number;
  state: string;
  context: Record<string, unknown>;
}

export interface Problem {
  code: string;
  message: string;
}

export interface Charge {
  receipt: string;
  amountCents: number;
  items: number;
  chargedAt: string;
}

export interface Exchange {
  op: "saveSnapshot" | "advanceSnapshot" | "loadSnapshot" | "sendSnapshot";
  variables: { i: Record<string, unknown> };
  response: unknown;
  ms: number;
}

export interface Edge {
  exchange: Exchange;
  to: string;
  charges: Charge[];
}

export interface Action {
  id: string;
  kind: "advance" | "send" | "save";
  trigger?: string;
  input?: Record<string, unknown>;
  preset?: "add-item" | "penny-total";
}

export interface MachineRecording {
  machine: MachineName;
  id: string;
  start: string;
  opening: Exchange[];
  nodes: Record<string, { snapshot: Snapshot; paid: number }>;
  edges: Record<string, Record<string, Edge>>;
}

export interface Recordings {
  queries: Record<string, string>;
  actions: Record<MachineName, Action[]>;
  maxItems: number;
  maxPaid: number;
  source: { file: string; code: string };
  machines: Record<MachineName, MachineRecording>;
}

/** One request the page sent and what came back, as the traffic panel lists it. */
export interface LogEntry {
  key: number;
  action: string | null;
  exchange: Exchange;
  snapshot: Snapshot | null;
  problem: Problem | null;
  /** The state the draft was in before the request. */
  from: string | null;
  /** Charges the payment provider took because of this request. */
  charges: Charge[];
  /** A send that returned the receipt the draft already had: the charge did not run again. */
  replayed: boolean;
}

export interface Session {
  machine: MachineName;
  node: string;
  log: LogEntry[];
  charges: Charge[];
}

/** What the server's answer to an exchange says. */
export function outcome(exchange: Exchange): { snapshot: Snapshot | null; problem: Problem | null } {
  const data = (exchange.response as { data: { dispatch: { stateMachine: Record<string, { output: { snapshot: string | null; problem: Problem | null } }> } } })
    .data.dispatch.stateMachine[exchange.op];
  return { snapshot: data.output.snapshot ? (JSON.parse(data.output.snapshot) as Snapshot) : null, problem: data.output.problem };
}

/** The page opening a machine: it loads the stored draft, finds none, and saves a first one. */
export function open(recordings: Recordings, machine: MachineName): Session {
  const m = recordings.machines[machine];
  const log = m.opening.map((exchange, i) => ({
    key: i,
    action: null,
    exchange,
    ...outcome(exchange),
    from: null,
    charges: [],
    replayed: false,
  }));
  return { machine, node: m.start, log, charges: [] };
}

export function snapshotOf(recordings: Recordings, session: Session): Snapshot {
  return recordings.machines[session.machine].nodes[session.node].snapshot;
}

/**
 * Whether a button can be pressed here, and if not, why. The page offers every trigger from every state; the
 * recording covers all of them, except an item past the second and a payment past the second, which it stops at.
 */
export function availability(recordings: Recordings, session: Session, actionId: string): { ok: boolean; reason?: string } {
  if (recordings.machines[session.machine].edges[session.node]?.[actionId]) return { ok: true };
  const snapshot = snapshotOf(recordings, session);
  const items = (snapshot.context.items as unknown[] | undefined) ?? [];
  if (actionId === "add-item" && items.length >= recordings.maxItems) return { ok: false, reason: `The recording stops at ${recordings.maxItems} items.` };
  if (actionId === "save-penny-total" && items.length === 0) return { ok: false, reason: "Add an item first." };
  if (actionId === "pay") return { ok: false, reason: `The recording stops at ${recordings.maxPaid} payments. Start over to pay again.` };
  return { ok: false, reason: "Not recorded." };
}

/** Presses a button: follows its recorded edge, logs the exchange and keeps any charge it caused. */
export function press(recordings: Recordings, session: Session, actionId: string): Session {
  const edge = recordings.machines[session.machine].edges[session.node]?.[actionId];
  if (!edge) return session;
  const before = snapshotOf(recordings, session);
  const result = outcome(edge.exchange);
  const receipt = (s: Snapshot | null) => (s?.context.receipt as string | null | undefined) ?? null;
  const entry: LogEntry = {
    key: session.log.length,
    action: actionId,
    exchange: edge.exchange,
    ...result,
    from: before.state,
    charges: edge.charges,
    replayed: edge.exchange.op === "sendSnapshot" && !!receipt(before) && receipt(before) === receipt(result.snapshot),
  };
  return { ...session, node: edge.to, log: [...session.log, entry], charges: [...edge.charges, ...session.charges] };
}

/** The GraphQL document an exchange sent, from the page's own transport. */
export function queryOf(recordings: Recordings, exchange: Exchange): string {
  return recordings.queries[exchange.op];
}

/** The lines (0-based, end exclusive) of a machine's Configure method in Machines.cs. */
export function configureRange(code: string, machine: MachineName): [number, number] {
  const lines = code.split("\n");
  const cls = lines.findIndex((l) => l.includes(`class ${machine === "turnstile" ? "TurnstileMachine" : "CheckoutMachine"}`));
  const start = lines.findIndex((l, i) => i > cls && l.includes("protected override void Configure"));
  const end = lines.findIndex((l, i) => i > start && l === "    }");
  return [start, end + 1];
}

/**
 * The line of Machines.cs that decided the last request, so the code panel can point at it: the move's `.On(...)`
 * when it moved, its `.When(...)` when the guard refused it, the target state's `.Holds(...)` when a rule refused
 * it, `.RunsOnce<ICharge>` for the charge, and the state's `m.In(...)` when there is no such move. -1 when none.
 */
export function lineOf(code: string, machine: MachineName, entry: LogEntry | undefined): number {
  if (!entry) return -1;
  const lines = code.split("\n");
  const [start, end] = configureRange(code, machine);
  const enumName = machine === "turnstile" ? "Turnstile" : "Checkout";
  const block = (state: string | null): [number, number] => {
    const at = lines.findIndex((l, i) => i >= start && i < end && l.includes(`m.In(${enumName}State.${state})`));
    if (at < 0) return [start, end];
    const next = lines.findIndex((l, i) => i > at && i < end && l.includes("m.In("));
    return [at, next < 0 ? end : next];
  };
  const find = (needle: string, [from, to]: [number, number]) => lines.findIndex((l, i) => i >= from && i < to && l.includes(needle));
  const i = entry.exchange.variables.i;
  const refusal = entry.problem?.code;

  if (entry.exchange.op === "loadSnapshot") return -1;
  if (entry.exchange.op === "saveSnapshot") {
    const state = entry.snapshot?.state ?? (JSON.parse(String(i.snapshot)) as Snapshot).state;
    return find(".Holds(", block(state));
  }
  const from = block(entry.from);
  if (entry.exchange.op === "sendSnapshot") {
    if (refusal === "no-transition") return find(`m.In(${enumName}State.${entry.from})`, from);
    return find(".RunsOnce<", [start, end]);
  }
  const trigger = String(i.trigger);
  const on = find(`.On(${enumName}Trigger.${trigger})`, from);
  if (refusal === "no-transition" || on < 0) return find(`m.In(${enumName}State.${entry.from})`, from);
  if (refusal === "guard-failed") return find(".When(", [on, from[1]]);
  if (refusal === "invalid-context") {
    const to = /^(\w+):/.exec(entry.problem?.message ?? "")?.[1];
    return find(".Holds(", block(to ?? null));
  }
  return on;
}
