// A machine-agnostic client for the four generic `stateMachine` GraphQL mutations. It knows nothing about
// any specific machine: the machine is an argument, the context is opaque JSON. One transport drives every
// machine. This is the frontend half of the "one set of operations, any machine" design.

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

export interface MachineInfo {
  name: string;
  hasEffect: boolean;
}

interface RawOutput {
  snapshot: string | null;
  problem: Problem | null;
}

const OUTPUT = "output { snapshot problem { code message } }";

export function createTransport(endpoint: string, getApiKey: () => string) {
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

  return {
    async listMachines(): Promise<MachineInfo[]> {
      const d = await call<{
        discover: { stateMachine: { listMachines: { machines: MachineInfo[] } } };
      }>(`{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }`);
      return d.discover.stateMachine.listMachines.machines;
    },

    async save(machine: string, id: string, snapshot: Snapshot): Promise<Result> {
      const d = await call<{ dispatch: { stateMachine: { saveSnapshot: { output: RawOutput } } } }>(
        `mutation($i: SaveSnapshotInput!){ dispatch { stateMachine { saveSnapshot(input:$i){ ${OUTPUT} } } } }`,
        { i: { machine, id, snapshot: JSON.stringify(snapshot) } },
      );
      return toResult(d.dispatch.stateMachine.saveSnapshot.output);
    },

    async advance(
      machine: string,
      id: string,
      trigger: string,
      input?: unknown,
      requestId?: string,
    ): Promise<Result> {
      const d = await call<{
        dispatch: { stateMachine: { advanceSnapshot: { output: RawOutput } } };
      }>(
        `mutation($i: AdvanceSnapshotInput!){ dispatch { stateMachine { advanceSnapshot(input:$i){ ${OUTPUT} } } } }`,
        { i: { machine, id, trigger, input: input ? JSON.stringify(input) : null, requestId } },
      );
      return toResult(d.dispatch.stateMachine.advanceSnapshot.output);
    },

    async load(machine: string, id: string): Promise<Result> {
      const d = await call<{ dispatch: { stateMachine: { loadSnapshot: { output: RawOutput } } } }>(
        `mutation($i: LoadSnapshotInput!){ dispatch { stateMachine { loadSnapshot(input:$i){ ${OUTPUT} } } } }`,
        { i: { machine, id } },
      );
      return toResult(d.dispatch.stateMachine.loadSnapshot.output);
    },

    async send(machine: string, id: string, requestId?: string): Promise<Result> {
      const d = await call<{ dispatch: { stateMachine: { sendSnapshot: { output: RawOutput } } } }>(
        `mutation($i: SendSnapshotInput!){ dispatch { stateMachine { sendSnapshot(input:$i){ ${OUTPUT} } } } }`,
        { i: { machine, id, requestId } },
      );
      return toResult(d.dispatch.stateMachine.sendSnapshot.output);
    },
  };
}

export type TraxTransport = ReturnType<typeof createTransport>;
