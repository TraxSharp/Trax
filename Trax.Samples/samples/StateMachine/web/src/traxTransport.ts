// A client for the two machines the host exposes, through their own typed operations under the `machines`
// GraphQL root: `mutation { machines { checkout { start addItem next back pay reset discard } } }` and
// `query { machines { checkout { get } } }`, and the same for the turnstile. Each event is its own mutation
// taking its own input type, and each answer carries the stored state, its typed context and its revision, or
// the problem the server refused it with. The page never writes a snapshot: the server builds the first one from
// the machine's declared start, and every later one by applying a declared event.
//
// Every call is also recorded for the page's "Under the hood" panel, with what the server did.

import { record } from "./inspectorLog";

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

export interface Result {
  snapshot: Snapshot | null;
  problem: Problem | null;
  /** The instance's id, which a start minted. */
  id?: string | null;
  /** The instance's revision after the operation, bumped by every write. */
  revision?: number | null;
}

/** A charge the server's payment provider took, as payments.listCharges reports it. */
export interface Charge {
  receipt: string;
  amountCents: number;
  items: number;
  chargedAt: string;
}

export interface MachineInfo {
  name: string;
  hasEffect: boolean;
}

/** What the page needs to know of each machine to write its documents. */
interface TypedMachine {
  /** Its field under `machines`. */
  field: string;
  /** The selection of its context, as GraphQL selects it. */
  context: string;
  /** The version the snapshots it rebuilds carry. */
  version: number;
  /** The field of the event bound to its effect, when it has one. */
  effect?: string;
}

const MACHINES: Record<string, TypedMachine> = {
  turnstile: { field: "turnstile", context: "paidWith", version: 1 },
  checkout: { field: "checkout", context: "items receipt total", version: 2, effect: "pay" },
};

interface TypedAnswer {
  id?: string | null;
  revision?: number | null;
  state?: string | null;
  context?: Record<string, unknown> | null;
  discarded?: boolean;
  problem: Problem | null;
}

// A trigger's field: the trigger in camelCase, as the server names it (AddItem is addItem).
const field = (trigger: string) => trigger[0].toLowerCase() + trigger.slice(1);

// A value as a GraphQL literal: objects with bare keys, everything else as JSON writes it.
const literal = (value: unknown): string => {
  if (value === null || value === undefined) return "null";
  if (Array.isArray(value)) return `[${value.map(literal).join(", ")}]`;
  if (typeof value === "object")
    return `{ ${Object.entries(value as Record<string, unknown>)
      .map(([k, v]) => `${k}: ${literal(v)}`)
      .join(", ")} }`;
  return JSON.stringify(value);
};

export function createTransport(endpoint: string, getApiKey: () => string, user: string) {
  async function call<T>(query: string): Promise<T> {
    const res = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Api-Key": getApiKey() },
      body: JSON.stringify({ query }),
    });
    const body = (await res.json()) as { data?: T; errors?: { message: string }[] };
    if (body.errors?.length) throw new Error(body.errors[0].message);
    return body.data as T;
  }

  // The typed answer as the snapshot the page shows: the context's null fields are the ones the state does not
  // hold, so they are left out.
  function toResult(machine: string, answer: TypedAnswer | null): Result {
    if (!answer) return { snapshot: null, problem: null };
    if (answer.problem || !answer.state) return { snapshot: null, problem: answer.problem, id: answer.id };
    const context = Object.fromEntries(
      Object.entries(answer.context ?? {}).filter(([, v]) => v !== null && v !== undefined),
    );
    return {
      snapshot: { machine, version: MACHINES[machine].version, state: answer.state, context },
      problem: null,
      id: answer.id,
      revision: answer.revision,
    };
  }

  /** Runs one typed operation of `machine`, and records the request and what came back. */
  async function operate(
    machine: string,
    operation: string,
    args: Record<string, unknown>,
    note: string,
    kind: "mutation" | "query" = "mutation",
  ): Promise<Result> {
    const typed = MACHINES[machine];
    const selection =
      operation === "discard"
        ? "id discarded problem { code message }"
        : `id revision state context { ${typed.context} } problem { code message }`;
    const argList = Object.entries(args)
      .filter(([, v]) => v !== undefined)
      .map(([k, v]) => `${k}: ${literal(v)}`)
      .join(", ");
    const query = `${kind === "mutation" ? "mutation " : ""}{ machines { ${typed.field} { ${operation}(${argList}) { ${selection} } } } }`;
    const label = `machines.${typed.field}.${operation}`;
    record({ channel: "http", direction: "out", user, title: `POST ${label}`, note, detail: { query } });

    const started = performance.now();
    try {
      const d = await call<{ machines: Record<string, Record<string, TypedAnswer | null>> }>(query);
      const answer = d.machines[typed.field][operation];
      const result = toResult(machine, answer);
      const missing = operation === "get" && answer === null;
      record({
        channel: "http",
        direction: "in",
        user,
        failed: !!result.problem,
        durationMs: Math.round(performance.now() - started),
        title: result.problem
          ? `Refused · ${result.problem.code}`
          : `200 OK · ${operation === "discard" ? "discarded" : missing ? "no draft" : result.snapshot?.state}`,
        note: result.problem
          ? `The server refused it: ${result.problem.message} Nothing was stored.`
          : outcome(operation, typed.effect, `${machine}/${String(args.id ?? result.id)}`, result),
        detail: answer,
      });
      return missing ? { snapshot: null, problem: { code: "not-found", message: "No draft with that id." } } : result;
    } catch (e) {
      record({
        channel: "http",
        direction: "in",
        user,
        failed: true,
        durationMs: Math.round(performance.now() - started),
        title: `Failed · ${label}`,
        note: e instanceof Error ? e.message : String(e),
      });
      throw e;
    }
  }

  return {
    async listMachines(): Promise<MachineInfo[]> {
      const d = await call<{
        discover: { stateMachine: { listMachines: { machines: MachineInfo[] } } };
      }>(`{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }`);
      return d.discover.stateMachine.listMachines.machines;
    },

    /** The charges the server's payment provider took from this user. The page reads them; it never makes one. */
    async listCharges(): Promise<Charge[]> {
      const query = "{ discover { payments { listCharges { charges { receipt amountCents items chargedAt } } } } }";
      record({
        channel: "http",
        direction: "out",
        user,
        title: "POST listCharges · payments",
        note: "Reads the charges the payment provider took for this user. It is a read: there is no endpoint that charges.",
        detail: { query },
      });
      const started = performance.now();
      const d = await call<{ discover: { payments: { listCharges: { charges: Charge[] } } } }>(query);
      const charges = d.discover.payments.listCharges.charges;
      record({
        channel: "http",
        direction: "in",
        user,
        durationMs: Math.round(performance.now() - started),
        title: `200 OK · ${charges.length} charge${charges.length === 1 ? "" : "s"}`,
        detail: d,
      });
      return charges;
    },

    start: (machine: string, requestId: string) =>
      operate(
        machine,
        "start",
        { requestId },
        `Starts a new ${machine} for this user from the machine's declared start. The server mints its id and builds its first snapshot; the page writes none. A retry with the same request id returns the same instance.`,
      ),

    advance: (machine: string, id: string, trigger: string, input: unknown, requestId: string) =>
      operate(
        machine,
        field(trigger),
        { id, input: input ?? undefined, requestId },
        `Sends ${trigger}, the machine's own mutation for it. The server runs the guard and the transition on its own stored copy of the draft, so the client cannot skip a rule.`,
      ),

    load: (machine: string, id: string) =>
      operate(
        machine,
        "get",
        { id },
        `Reads this user's stored ${machine}, migrating it forward if an older version of the machine wrote it.`,
        "query",
      ),

    send: (machine: string, id: string, requestId?: string) =>
      operate(
        machine,
        MACHINES[machine].effect ?? "pay",
        { id, requestId: requestId ?? crypto.randomUUID() },
        `Sends the event bound to the machine's one effect. The server runs the effect itself, exactly once per request id, and only from that event's source state; the page sends no amount and no receipt.`,
      ),

    discard: (machine: string, id: string, requestId: string) =>
      operate(machine, "discard", { id, requestId }, `Deletes this user's ${machine}, so the next visit starts a new one.`),
  };
}

// The last receipt each draft's send came back with, so a repeat that changed nothing can say so.
const receipts = new Map<string, string>();

function outcome(operation: string, effect: string | undefined, draft: string, result: Result): string | undefined {
  if (operation === "discard") return "Deleted, for this user only.";
  const snapshot = result.snapshot;
  if (!snapshot) return operation === "get" ? "No draft stored yet for this user." : undefined;
  const receipt = snapshot.context.receipt as string | null | undefined;
  const sending = operation === effect;
  // A draft loaded already paid carries its receipt: a send that returns it again charged nothing.
  if (!sending && receipt) receipts.set(draft, receipt);
  if (sending && receipt) {
    const repeat = receipts.get(draft) === receipt;
    receipts.set(draft, receipt);
    return repeat
      ? `Same receipt as before (${receipt}): the effect had already run, so the server did not charge again.`
      : `Stored as ${snapshot.state} at revision ${result.revision}; the charge ran once and returned receipt ${receipt}.`;
  }
  if (operation === "start") return `Started in ${snapshot.state}, under an id the server minted.`;
  return `Stored as ${snapshot.state} at revision ${result.revision}.`;
}

export type TraxTransport = ReturnType<typeof createTransport>;
