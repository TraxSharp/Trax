import { print, type DocumentNode } from "graphql";
import { createClient } from "graphql-ws";

// Minimal fetch-based GraphQL client for the e2e suite (node env, no urql/ws). Points at the
// disposable e2e devhost that `npm run test:e2e` brings up.
export const E2E_URL = process.env.TRAX_E2E_URL ?? "http://localhost:5311/trax/graphql";
export const E2E_KEY = process.env.TRAX_E2E_KEY ?? "admin-key-do-not-use-in-production";

export async function gql<T = Record<string, unknown>>(
  doc: DocumentNode | string,
  variables?: Record<string, unknown>,
): Promise<T> {
  const query = typeof doc === "string" ? doc : print(doc);
  const res = await fetch(E2E_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": E2E_KEY },
    body: JSON.stringify({ query, variables }),
  });
  const json = (await res.json()) as { data?: T; errors?: unknown };
  if (json.errors) throw new Error(`${JSON.stringify(json.errors)}`);
  return json.data as T;
}

export async function reachable(): Promise<boolean> {
  try {
    await gql("{ __typename }");
    return true;
  } catch {
    return false;
  }
}

/** Walk a dot path into a response object. */
export function pick(obj: unknown, path: string): unknown {
  return path
    .split(".")
    .reduce<unknown>((acc, key) => (acc as Record<string, unknown> | undefined)?.[key], obj);
}

/** Items array at a dot path (throws a clear error if it isn't there). */
export function items(obj: unknown, path: string): { id: number }[] {
  const value = pick(obj, path);
  if (!Array.isArray(value)) throw new Error(`expected an array at ${path}`);
  return value as { id: number }[];
}

/** The devhost's origin (for its /dev endpoints). */
export const E2E_ORIGIN = new URL(E2E_URL).origin;

/**
 * Subscribe over graphql-ws (the API key rides in connection_init, as in the dashboard) and
 * resolve with the first event `accept` takes, or reject after `timeoutMs`.
 */
export function nextEvent<T = Record<string, unknown>>(
  doc: DocumentNode,
  variables: Record<string, unknown>,
  accept: (data: T) => boolean,
  timeoutMs: number,
): { ready: Promise<void>; event: Promise<T>; dispose: () => void } {
  const client = createClient({
    url: E2E_URL.replace(/^http/, "ws"),
    webSocketImpl: WebSocket,
    connectionParams: { apiKey: E2E_KEY },
    lazy: false,
  });
  let markReady: () => void = () => {};
  const ready = new Promise<void>((resolve) => (markReady = resolve));
  client.on("connected", () => markReady());
  const event = new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`no matching event within ${timeoutMs}ms`)), timeoutMs);
    client.subscribe<T>(
      { query: print(doc), variables },
      {
        next: (msg) => {
          if (msg.errors) {
            clearTimeout(timer);
            reject(new Error(JSON.stringify(msg.errors)));
          } else if (msg.data && accept(msg.data as T)) {
            clearTimeout(timer);
            resolve(msg.data as T);
          }
        },
        error: (e) => {
          clearTimeout(timer);
          reject(e instanceof Error ? e : new Error(JSON.stringify(e)));
        },
        complete: () => {},
      },
    );
  });
  return { ready, event, dispose: () => void client.dispose() };
}
