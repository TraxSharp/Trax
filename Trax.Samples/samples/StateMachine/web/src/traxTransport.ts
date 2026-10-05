// A machine-agnostic client for the four generic `stateMachine` GraphQL mutations. It knows nothing about
// any specific machine: the machine is an argument, the context is opaque JSON. One transport drives every
// machine. This is the frontend half of the "one set of operations, any machine" design.
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

interface RawOutput {
  snapshot: string | null;
  problem: Problem | null;
}

interface RawResponse {
  externalId: string | null;
  output: RawOutput;
}

type Mutation = "saveSnapshot" | "advanceSnapshot" | "loadSnapshot" | "sendSnapshot";

const OUTPUT = "externalId output { snapshot problem { code message } }";

const INPUT_TYPES: Record<Mutation, string> = {
  saveSnapshot: "SaveSnapshotInput",
  advanceSnapshot: "AdvanceSnapshotInput",
  loadSnapshot: "LoadSnapshotInput",
  sendSnapshot: "SendSnapshotInput",
};

export function createTransport(endpoint: string, getApiKey: () => string, user: string) {
  async function call<T>(query: string, variables?: unknown): Promise<T> {
    const res = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Api-Key": getApiKey() },
      body: JSON.stringify({ query, variables }),
    });
    const body = (await res.json()) as { data?: T; errors?: { message: string }[] };
    if (body.errors?.length) throw new Error(body.errors[0].message);
    return body.data as T;
  }

  // The snapshot crosses the wire as canonical JSON in a string field; parse it back into an object.
  function toResult(raw: RawOutput): Result {
    return {
      snapshot: raw.snapshot ? (JSON.parse(raw.snapshot) as Snapshot) : null,
      problem: raw.problem,
    };
  }

  /** Runs one of the four mutations, and records the request and what came back. */
  async function mutate(mutation: Mutation, input: Record<string, unknown>, note: string): Promise<Result> {
    const query = `mutation($i: ${INPUT_TYPES[mutation]}!){ dispatch { stateMachine { ${mutation}(input:$i){ ${OUTPUT} } } } }`;
    const variables = { i: input };
    const label = `${mutation} · ${input.machine}${input.trigger ? ` · ${input.trigger}` : ""}`;
    record({ channel: "http", direction: "out", user, title: `POST ${label}`, note, detail: { query, variables } });

    const started = performance.now();
    try {
      const d = await call<{ dispatch: { stateMachine: Record<Mutation, RawResponse> } }>(query, variables);
      const raw = d.dispatch.stateMachine[mutation];
      const result = toResult(raw.output);
      const runId = raw.externalId ?? undefined;
      record({
        channel: "http",
        direction: "in",
        user,
        runId,
        failed: !!result.problem,
        durationMs: Math.round(performance.now() - started),
        title: result.problem ? `Refused · ${result.problem.code}` : `200 OK · ${result.snapshot?.state ?? "no draft"}`,
        note: result.problem
          ? `The server refused it: ${result.problem.message} Nothing was stored.`
          : outcome(mutation, `${input.machine}/${input.id}`, result.snapshot),
        detail: { externalId: raw.externalId, problem: raw.output.problem, snapshot: raw.output.snapshot },
      });
      return result;
    } catch (e) {
      record({
        channel: "http",
        direction: "in",
        user,
        failed: true,
        durationMs: Math.round(performance.now() - started),
        title: `Failed · ${mutation}`,
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

    save: (machine: string, id: string, snapshot: Snapshot) =>
      mutate(
        "saveSnapshot",
        { machine, id, snapshot: JSON.stringify(snapshot) },
        `Saves the whole draft as the client wrote it. The server checks it against the ${machine} machine's rules for the ${snapshot.state} state before storing it, for this user only.`,
      ),

    advance: (machine: string, id: string, trigger: string, input?: unknown, requestId?: string) =>
      mutate(
        "advanceSnapshot",
        { machine, id, trigger, input: input ? JSON.stringify(input) : null, requestId },
        `Asks the server to fire ${trigger} on its own stored copy of the draft: it runs the guard and the transition itself, so the client cannot skip a rule.`,
      ),

    load: (machine: string, id: string) =>
      mutate(
        "loadSnapshot",
        { machine, id },
        `Loads this user's stored ${machine} draft, migrating it forward if an older version of the machine wrote it.`,
      ),

    send: (machine: string, id: string, requestId?: string) =>
      mutate(
        "sendSnapshot",
        { machine, id, requestId },
        `Fires the machine's one effect-bearing transition on the stored draft. The server runs the effect itself, exactly once, and only from that transition's source state.`,
      ),
  };
}

// The last receipt each draft's send came back with, so a repeat that changed nothing can say so.
const receipts = new Map<string, string>();

function outcome(mutation: Mutation, draft: string, snapshot: Snapshot | null): string | undefined {
  if (!snapshot) return mutation === "loadSnapshot" ? "No draft stored yet for this user." : undefined;
  const receipt = snapshot.context.receipt as string | null | undefined;
  // A draft loaded already paid carries its receipt: a send that returns it again charged nothing.
  if (mutation !== "sendSnapshot" && receipt) receipts.set(draft, receipt);
  if (mutation === "sendSnapshot" && receipt) {
    const repeat = receipts.get(draft) === receipt;
    receipts.set(draft, receipt);
    return repeat
      ? `Same receipt as before (${receipt}): the effect had already run, so the server did not charge again.`
      : `Stored as ${snapshot.state}; the charge ran once and returned receipt ${receipt}.`;
  }
  return `Stored as ${snapshot.state} (machine version ${snapshot.version}).`;
}

export type TraxTransport = ReturnType<typeof createTransport>;
