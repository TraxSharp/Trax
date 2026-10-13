/**
 * The typed `machines` root, behind the same {@link SnapshotExecutor} seam as the generic mutations.
 *
 * A host serves each machine marked `[TraxMachine]` as its own typed operations:
 * `mutation { machines { <machine> { start discard <event> } } }` and `query { machines { <machine> { get } } }`,
 * each answering with the instance's `id revision state context` or a `problem`. This executor writes those
 * documents for the machines it is told about, so {@link createSnapshotClient}, {@link createDraftSession} and
 * {@link startDraft} drive a typed machine exactly as they drive the generic mutations, and a page moves from one to
 * the other by swapping its executor.
 *
 * Every value is written into the document as a literal, so no document names a generated input type. The answer's
 * typed context is rebuilt into the snapshot the twin reads: the fields the answer holds, with the null ones left
 * out, since a typed context cannot tell a field stored as null from one the state does not hold. The server compares
 * a `clientResult` canonically with a null member equal to an absent one, so a prediction built from it matches.
 *
 * There is no typed save, and no schema hash: a machine served typed moves only through its events, and its version is
 * checked through its persisted documents. Given the machine's `documents` (the host's manifest), the executor sends
 * each operation by its persisted id with its values as variables, never a document: a host that requires persisted
 * operations refuses anything else. A document of another version of the machine is answered `client-outdated` (the
 * page reloads) or `server-outdated` (retry), and the executor returns either as a problem.
 */
import type { MachineSpec, TypedMachine } from '../typed';
import type { HttpExecutorOptions, SnapshotExecutor, SnapshotOutput } from './transport';

/**
 * One entry of a host's machine documents manifest, as `query { operations { machineDocuments { id machine operation
 * irHash document } } }` lists it: the persisted document of one operation of one exposed machine.
 */
export interface MachineDocument {
  /** The persisted id the client sends. */
  id: string;
  /** The machine's id (`topic-map`). */
  machine: string;
  /** The operation: `start`, `discard`, `get`, or an event's field name (`chooseFields`). */
  operation: string;
  /** The hash of the machine's IR the document was generated for, which the twin's `irHash` must equal. */
  irHash: string;
  /** The document's text, which declares the variables it takes. */
  document?: string;
}

/** The codes a host answers a machine document of another version with. */
export const OUTDATED_CODES: readonly string[] = ['client-outdated', 'server-outdated'];

/** What the executor needs to know about one exposed machine. */
export interface TypedMachineOperations {
  /** The machine: its id names its field under `machines`, and its version stamps the snapshots rebuilt. */
  machine: TypedMachine<MachineSpec> | { id: string; version: number };
  /**
   * The machine's persisted documents, from the host's manifest. Given, every operation is sent by its id with its
   * values as variables, and {@link context} is not read: each document selects the whole answer. Each must be of
   * this machine and, for a {@link TypedMachine} that carries its IR hash, of the IR the twin was generated from;
   * otherwise {@link createMachineExecutor} throws.
   */
  documents?: readonly MachineDocument[];
  /**
   * The selection of the machine's context, as GraphQL selects it: `"title pages note"`, with a sub-selection for
   * a nested record (`"address { street }"`). Omit for a machine whose states declare no context record.
   */
  context?: string;
  /**
   * The event bound to the machine's irreversible effect (`"Pay"`), which `sendSnapshot` fires. Omit for a machine
   * with no effect.
   */
  effectEvent?: string;
}

export interface MachineExecutorOptions extends HttpExecutorOptions {
  /** Every machine the page drives, by any order; a machine not listed is answered `unknown-machine`. */
  machines: TypedMachineOperations[];
}

/** The machine's field under `machines`: its kebab-case id in camelCase (`topic-map` is `topicMap`). */
export const machineField = (machineId: string): string =>
  machineId
    .split('-')
    .filter((part) => part.length > 0)
    .map((part, i) => (i === 0 ? part : part[0].toUpperCase() + part.slice(1)))
    .join('');

/** An event's field: the trigger in camelCase (`CancelBuild` is `cancelBuild`), as the server names it. */
export const eventField = (trigger: string): string => {
  // The server's camelCase lowers the leading run of capitals, keeping the last one of a run that a lowercase
  // letter follows (`ID` is `id`, `URLPath` is `urlPath`).
  let upper = 0;
  while (upper < trigger.length && trigger[upper] >= 'A' && trigger[upper] <= 'Z') upper++;
  if (upper === 0) return trigger;
  if (upper === 1 || upper === trigger.length) return trigger.slice(0, upper).toLowerCase() + trigger.slice(upper);
  return trigger.slice(0, upper - 1).toLowerCase() + trigger.slice(upper - 1);
};

/** A JSON value as a GraphQL literal: objects with bare keys, everything else as JSON writes it. */
export const graphqlLiteral = (value: unknown): string => {
  if (value === null || value === undefined) return 'null';
  if (Array.isArray(value)) return `[${value.map(graphqlLiteral).join(', ')}]`;
  if (typeof value === 'object')
    return `{ ${Object.entries(value as Record<string, unknown>)
      .filter(([, v]) => v !== undefined)
      .map(([k, v]) => `${k}: ${graphqlLiteral(v)}`)
      .join(', ')} }`;
  return JSON.stringify(value);
};

interface TypedAnswer {
  id?: string | null;
  revision?: number | null;
  dueAt?: string | null;
  /** False on a replay the instance has moved past: the server cannot know its due time, and `dueAt` means nothing. */
  dueAtKnown?: boolean | null;
  state?: string | null;
  context?: Record<string, unknown> | null;
  discarded?: boolean;
  problem?: { code: string; message: string } | null;
}

// A context with every null member left out, at every depth.
export const withoutNulls = (value: unknown): unknown => {
  if (Array.isArray(value)) return value.map(withoutNulls);
  if (value !== null && typeof value === 'object')
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([, v]) => v !== null && v !== undefined)
        .map(([k, v]) => [k, withoutNulls(v)]),
    );
  return value;
};

const parseInput = (input: unknown): Record<string, unknown> | undefined => {
  if (typeof input !== 'string' || input.length === 0) return undefined;
  const parsed = JSON.parse(input) as unknown;
  return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
    ? (parsed as Record<string, unknown>)
    : undefined;
};

const problem = (code: string, message: string): SnapshotOutput => ({
  snapshot: null,
  problem: { code, message },
});

// The variables a document declares (`$id: UUID!` declares `id`), or null when its text is not known.
const declaredVariables = (document: string | undefined): Set<string> | null =>
  document === undefined ? null : new Set([...document.matchAll(/\$(\w+)\s*:/g)].map((m) => m[1]));

// Checks a machine's documents against the machine, keyed by operation. A document of another machine, or of another
// IR than the twin's, is refused here rather than sent: the host would answer it client-outdated on every request.
const documentsOf = (typed: TypedMachineOperations): Map<string, MachineDocument> | null => {
  if (!typed.documents) return null;
  const machineId = typed.machine.id;
  const irHash = 'schemaHash' in typed.machine ? typed.machine.schemaHash : '';
  const byOperation = new Map<string, MachineDocument>();
  for (const document of typed.documents) {
    if (document.machine !== machineId)
      throw new Error(
        `The document '${document.id}' is of the machine '${document.machine}', not '${machineId}'. Pass each machine its own documents.`,
      );
    if (irHash && document.irHash !== irHash)
      throw new Error(
        `The document '${document.id}' was generated for another version of '${machineId}' (IR ${document.irHash}) than its twin (IR ${irHash}). Export the documents again from a host that runs this version.`,
      );
    byOperation.set(document.operation, document);
  }
  return byOperation;
};

type Answered = { answer: TypedAnswer | null | undefined } | { refused: SnapshotOutput };

/**
 * An executor over plain `fetch` for the typed `machines` root. Pass it to {@link createSnapshotClient}; the
 * sessions above it are unchanged. A GraphQL error (an unauthenticated caller, a document the schema refuses) throws,
 * which the session reports as a transport error; a refusal is a problem, as data, and so is `client-outdated` or
 * `server-outdated`, the host's answer to a persisted document of another version of the machine.
 */
export function createMachineExecutor(
  endpoint: string,
  options: MachineExecutorOptions,
): SnapshotExecutor {
  const doFetch = options.fetch ?? globalThis.fetch;
  const machines = new Map(options.machines.map((m) => [m.machine.id, { ...m, byOperation: documentsOf(m) }]));

  const post = async (body: Record<string, unknown>, name: string): Promise<Answered> => {
    const response = await doFetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...options.headers?.() },
      body: JSON.stringify(body),
    });
    const answered = (await response.json()) as {
      data?: { machines?: Record<string, Record<string, unknown>> } | null;
      errors?: { message: string; extensions?: { code?: string } }[];
    };
    const error = answered.errors?.[0];
    if (error) {
      const code = error.extensions?.code;
      if (code && OUTDATED_CODES.includes(code)) return { refused: problem(code, error.message) };
      throw new Error(error.message);
    }
    return { answer: Object.values(answered.data?.machines ?? {})[0]?.[name] as TypedAnswer | null | undefined };
  };

  return async (operation, input) => {
    const machineId = String(input.machine);
    const typed = machines.get(machineId);
    if (!typed) return problem('unknown-machine', `No typed machine named '${machineId}' is configured.`);

    const field = machineField(machineId);
    const result = `id revision state dueAt dueAtKnown ${typed.context ? `context { ${typed.context} } ` : ''}problem { code message }`;
    const args = (values: Record<string, unknown>) =>
      Object.entries(values)
        .filter(([, v]) => v !== undefined)
        .map(([k, v]) => `${k}: ${graphqlLiteral(v)}`)
        .join(', ');

    // The answer as the generic mutations give it: the snapshot as JSON, the twin's shape.
    const output = (answer: TypedAnswer | null | undefined): SnapshotOutput | null => {
      if (!answer) return null;
      if (answer.problem) return { snapshot: null, problem: answer.problem, id: answer.id ?? null };
      return {
        snapshot: JSON.stringify({
          machine: machineId,
          version: typed.machine.version,
          state: answer.state,
          context: withoutNulls(answer.context ?? {}),
        }),
        problem: null,
        id: answer.id ?? null,
        revision: answer.revision ?? null,
        // A due time the server says it cannot know is left out, so it is never read as null ("no timer").
        ...(answer.dueAtKnown === false ? {} : { dueAt: answer.dueAt }),
      };
    };

    // One operation: by its persisted id, its values as variables, when the machine has documents; else inline.
    const run = async (
      keyword: 'mutation' | 'query',
      name: string,
      values: Record<string, unknown>,
      selection = result,
    ): Promise<Answered> => {
      if (typed.byOperation) {
        const document = typed.byOperation.get(name);
        if (!document)
          return {
            refused: problem(
              'client-outdated',
              `There is no document for '${machineId}.${name}': the documents are of another version of the machine.`,
            ),
          };
        const declared = declaredVariables(document.document);
        const variables = Object.fromEntries(
          Object.entries(values).filter(([k, v]) => v !== undefined && (declared === null || declared.has(k))),
        );
        return post({ id: document.id, variables }, name);
      }
      const root = keyword === 'mutation' ? 'mutation ' : '';
      return post({ query: `${root}{ machines { ${field} { ${name}(${args(values)}) { ${selection} } } } }` }, name);
    };

    const answer = async (answered: Promise<Answered>) => {
      const done = await answered;
      return 'refused' in done ? done.refused : output(done.answer);
    };

    switch (operation) {
      case 'startSnapshot':
        return answer(run('mutation', 'start', { input: parseInput(input.input), requestId: input.requestId }));

      case 'advanceSnapshot':
        return answer(
          run('mutation', eventField(String(input.trigger)), {
            id: input.id,
            input: parseInput(input.input),
            requestId: input.requestId,
            clientResult: input.clientResult ?? undefined,
          }),
        );

      case 'sendSnapshot':
        if (!typed.effectEvent) return problem('no-effect', `Machine '${machineId}' has no effect to send.`);
        return answer(
          run('mutation', eventField(typed.effectEvent), {
            id: input.id,
            requestId: input.requestId ?? `send:${String(input.id)}`,
          }),
        );

      case 'loadSnapshot': {
        const done = await run('query', 'get', { id: input.id });
        if ('refused' in done) return done.refused;
        return done.answer ? output(done.answer) : problem('not-found', 'No draft with that id.');
      }

      case 'discardSnapshot': {
        const done = await run(
          'mutation',
          'discard',
          { id: input.id, requestId: input.requestId },
          'id discarded problem { code message }',
        );
        if ('refused' in done) return done.refused;
        const discarded = done.answer;
        return discarded
          ? { snapshot: null, problem: discarded.problem ?? null, discarded: discarded.discarded ?? false }
          : null;
      }

      case 'saveSnapshot':
        return problem(
          'save-not-allowed',
          `Machine '${machineId}' is served typed: it moves only through its declared events.`,
        );
    }
  };
}
