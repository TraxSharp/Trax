// Plays back the StateMachine sample's recordings (src/data/state-machine-recordings.json, written by Trax.Samples'
// scripts/recordings/state-machine.mjs). Each machine was recorded as a graph: a node is a draft's state and
// context, an edge is one of the page's buttons pressed there, with the request the page sent and the server's
// whole response. The page drives each machine through its own operations under the GraphQL `machines` root:
// `start` opens a draft (the server mints its id), and each event is a mutation of its own. Pressing a button
// follows its edge, so every answer shown is one the server gave at that point.

export type MachineName = "turnstile" | "checkout";

/** A draft as the page shows it: the context without the fields its state does not hold. */
export interface Snapshot {
  machine: string;
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

/** One request: the machine operation's name, the GraphQL document the page sent, and the server's answer. */
export interface Exchange {
  operation: string;
  query: string;
  response: unknown;
  ms: number;
}

/** What a machine operation answers. */
export interface Answer {
  id: string | null;
  revision: number | null;
  state: string | null;
  context: Record<string, unknown> | null;
  problem: Problem | null;
}

export interface Edge {
  exchange: Exchange;
  to: string;
  charges: Charge[];
}

export interface Action {
  id: string;
  kind: "event";
  trigger: string;
  input?: Record<string, unknown>;
  preset?: "add-item";
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

/**
 * What an exchange was: the draft starting, an event that moved it (or left it where it was), an event that ran
 * the charge, a second Pay that returned the first one's receipt, or a refusal.
 */
export type Kind = "start" | "move" | "charge" | "replay" | "refused";

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
  /** An event that returned the receipt the draft already had: the charge did not run again. */
  replayed: boolean;
  kind: Kind;
}

export interface Session {
  machine: MachineName;
  node: string;
  log: LogEntry[];
  charges: Charge[];
}

/** The machine an exchange went to and the operation's answer, read from the server's response. */
export function answerOf(exchange: Exchange): { machine: string; answer: Answer } {
  const machines = (exchange.response as { data: { machines: Record<string, Record<string, Answer>> } }).data.machines;
  const [machine, operations] = Object.entries(machines)[0];
  return { machine, answer: operations[exchange.operation] };
}

/** What the server's answer to an exchange says, with the context as the page shows it. */
export function outcome(exchange: Exchange): { snapshot: Snapshot | null; problem: Problem | null } {
  const { machine, answer } = answerOf(exchange);
  if (answer.problem || !answer.state) return { snapshot: null, problem: answer.problem };
  const context = Object.fromEntries(Object.entries(answer.context ?? {}).filter(([, v]) => v !== null && v !== undefined));
  return { snapshot: { machine, state: answer.state, context }, problem: null };
}

/** The page opening a machine: it starts a draft, whose id the server mints. */
export function open(recordings: Recordings, machine: MachineName): Session {
  const m = recordings.machines[machine];
  const log = m.opening.map(
    (exchange, i): LogEntry => ({
      key: i,
      action: null,
      exchange,
      ...outcome(exchange),
      from: null,
      charges: [],
      replayed: false,
      kind: "start",
    }),
  );
  return { machine, node: m.start, log, charges: [] };
}

export function snapshotOf(recordings: Recordings, session: Session): Snapshot {
  return recordings.machines[session.machine].nodes[session.node].snapshot;
}

/**
 * Whether a button can be pressed here, and if not, why. The page offers every event from every state; the
 * recording covers all of them, except an item past the second and a payment past the second, which it stops at.
 */
export function availability(recordings: Recordings, session: Session, actionId: string): { ok: boolean; reason?: string } {
  if (recordings.machines[session.machine].edges[session.node]?.[actionId]) return { ok: true };
  const snapshot = snapshotOf(recordings, session);
  const items = (snapshot.context.items as unknown[] | undefined) ?? [];
  if (actionId === "add-item" && items.length >= recordings.maxItems) return { ok: false, reason: `The recording stops at ${recordings.maxItems} items.` };
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
  const replayed = !result.problem && edge.charges.length === 0 && !!receipt(before) && receipt(before) === receipt(result.snapshot);
  const kind: Kind = result.problem ? "refused" : edge.charges.length > 0 ? "charge" : replayed ? "replay" : "move";
  const entry: LogEntry = {
    key: session.log.length,
    action: actionId,
    exchange: edge.exchange,
    ...result,
    from: before.state,
    charges: edge.charges,
    replayed,
    kind,
  };
  return { ...session, node: edge.to, log: [...session.log, entry], charges: [...edge.charges, ...session.charges] };
}

/** An operation's event, as Machines.cs names it: `addItem` is `AddItem`. */
export function triggerOf(operation: string): string {
  return operation[0].toUpperCase() + operation.slice(1);
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
 * The line of Machines.cs that decided the last request, so the code panel can point at it: `.StartsAt(...)` for
 * the start, the move's `.On(...)` when it moved, its `.When(...)` when the guard refused it, the target state's
 * `.Requires(...)` when a rule refused it, `.RunsOnce<ICharge>` for the charge, and the state's `m.In(...)` when
 * there is no such move. -1 when none.
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
  const refusal = entry.problem?.code;

  if (entry.kind === "start") return find(".StartsAt(", [start, end]);
  if (entry.kind === "charge" || entry.kind === "replay") return find(".RunsOnce<", [start, end]);
  const from = block(entry.from);
  const on = find(`.On(${enumName}Trigger.${triggerOf(entry.exchange.operation)})`, from);
  if (refusal === "no-transition" || on < 0) return find(`m.In(${enumName}State.${entry.from})`, from);
  const nextOn = find(".On(", [on + 1, from[1]]);
  const move: [number, number] = [on, nextOn < 0 ? from[1] : nextOn];
  if (refusal === "guard-failed") return find(".When(", move);
  if (refusal === "invalid-context") {
    const to = /^(\w+):/.exec(entry.problem?.message ?? "")?.[1];
    const rule = find(".Requires(", block(to ?? entry.from));
    return rule >= 0 ? rule : find(`m.In(${enumName}State.${entry.from})`, from);
  }
  if (refusal) return find(`m.In(${enumName}State.${entry.from})`, from);
  return on;
}
