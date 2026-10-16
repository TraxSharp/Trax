// Where the host is, who is signed in, and what the page sends. The generated client (./trax) holds every operation
// by the persisted id the host registered, and sends only that id and its variables: the host refuses a document sent
// as text. The "Under the hood" panel shows each request with the document its id names, read from the manifest the
// client was generated from and from the page's own documents, so what the server runs is visible without the page
// ever sending it.

import { paymentsListChargesDocumentId } from "./trax/documents/paymentsListCharges.g";
import { stateMachineListMachinesDocumentId } from "./trax/documents/stateMachineListMachines.g";
import { configureTrax } from "./trax/index.g";
import manifest from "./trax/manifest.json";
import pageDocuments from "./trax/trax.documents.json";
import { record } from "./inspectorLog";

export const ENDPOINT = "http://localhost:5280/trax/graphql";
const SOCKET = "ws://localhost:5280/trax/graphql";

export interface User {
  label: string;
  key: string;
}

let signedIn: User | null = null;

/**
 * Points the generated client at the host as `user`. Switching users closes the socket and opens a new one, and the
 * remembered machines move to the new user's instances in the same render.
 */
export function signIn(user: User): void {
  signedIn = user;
  configureTrax({
    url: ENDPOINT,
    socketUrl: SOCKET,
    headers: () => ({ "X-Api-Key": user.key }),
    connectionParams: () => ({ apiKey: user.key }),
    user: user.label,
  });
}

interface Known {
  /** What the id is, as the panel names it (`checkout · pay`). */
  label: string;
  document: string;
}

// Every id the client can send, from the manifest it was generated from.
const known = new Map<string, Known>();
for (const machine of manifest.machines)
  for (const d of machine.documents) known.set(d.id, { label: `${machine.id} · ${d.operation}`, document: d.document });
// The page's own documents, one per train it calls: the host registers them from trax.documents.json.
for (const [id, name] of [
  [paymentsListChargesDocumentId, "PaymentsListCharges"],
  [stateMachineListMachinesDocumentId, "StateMachineListMachines"],
] as const) {
  const d = pageDocuments.documents.find((document) => document.name === name);
  if (d) known.set(id, { label: `document · ${name}`, document: d.text });
}

// The first problem an answer carries, wherever it sits: a machine operation answers one beside its snapshot.
function problemIn(value: unknown): { code: string; message: string } | null {
  if (value === null || typeof value !== "object") return null;
  const problem = (value as { problem?: unknown }).problem;
  if (problem && typeof problem === "object" && "code" in problem) return problem as { code: string; message: string };
  for (const child of Object.values(value)) {
    const found = problemIn(child);
    if (found) return found;
  }
  return null;
}

const send = window.fetch.bind(window);

/** Records every request the generated client makes to the host, and what came back. */
window.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
  const url = input instanceof Request ? input.url : String(input);
  if (url !== ENDPOINT || typeof init?.body !== "string") return send(input, init);

  const user = signedIn?.label ?? "nobody";
  const sent = JSON.parse(init.body) as { id?: string; variables?: Record<string, unknown> };
  const operation = sent.id ? known.get(sent.id) : undefined;
  record({
    channel: "http",
    direction: "out",
    user,
    title: `POST ${operation?.label ?? "an unknown id"}`,
    note: "Sent by its persisted id and its variables only. The document below is the one the host registered under that id; the page does not send it.",
    detail: { id: sent.id, variables: sent.variables, document: operation?.document },
  });

  const started = performance.now();
  try {
    const response = await send(input, init);
    const body = (await response.clone().json().catch(() => null)) as {
      data?: unknown;
      errors?: { message: string; extensions?: { code?: string } }[];
    } | null;
    const error = body?.errors?.[0];
    const problem = problemIn(body?.data);
    record({
      channel: "http",
      direction: "in",
      user,
      failed: !!error || !!problem,
      durationMs: Math.round(performance.now() - started),
      title: error
        ? `Error · ${error.extensions?.code ?? response.status}`
        : problem
          ? `Refused · ${problem.code}`
          : `${response.status} OK`,
      note: error?.message ?? (problem ? `${problem.message} Nothing was stored.` : undefined),
      detail: body,
    });
    return response;
  } catch (e) {
    record({
      channel: "http",
      direction: "in",
      user,
      failed: true,
      durationMs: Math.round(performance.now() - started),
      title: `Failed · ${operation?.label ?? "request"}`,
      note: e instanceof Error ? e.message : String(e),
    });
    throw e;
  }
};
