// Sends a console document by its persisted id when the host registered one for it.
//
// A host that requires persisted operations refuses document text, so for each operation the
// runtime config names, the request carries the id instead: over HTTP the body is
// { id, operationName, variables, extensions } with no query, and over the socket the subscribe
// payload has an empty query, which persistedMessageReplacer then leaves out (graphql-ws requires
// the field, the host refuses it beside an id). An operation the config does not name, such as
// persisted-operation management, is sent as text, as before.

export type DocumentIds = Readonly<Record<string, string>>;

interface GraphQLBody {
  operationName?: string | null;
  query?: string;
  variables?: unknown;
  extensions?: unknown;
}

/** The request body to send for `body`: by id when `ids` names its operation, else unchanged. */
export function persistedBody(body: string, ids: DocumentIds): string {
  let parsed: GraphQLBody;
  try {
    parsed = JSON.parse(body) as GraphQLBody;
  } catch {
    return body;
  }
  const id = parsed.operationName ? ids[parsed.operationName] : undefined;
  if (!id) return body;
  return JSON.stringify({
    id,
    operationName: parsed.operationName,
    variables: parsed.variables,
    extensions: parsed.extensions,
  });
}

/**
 * A fetch that sends each request whose operation has an id by that id, or undefined when the host
 * named none, so urql keeps its own. The global fetch is looked up per request, not captured.
 */
export function persistedFetch(ids: DocumentIds, inner?: typeof fetch): typeof fetch | undefined {
  if (Object.keys(ids).length === 0) return undefined;
  return (input, init) =>
    (inner ?? globalThis.fetch)(
      input,
      typeof init?.body === "string" ? { ...init, body: persistedBody(init.body, ids) } : init,
    );
}

interface SubscribePayload {
  query: string;
  operationName?: string | null;
  variables?: Record<string, unknown> | null;
  extensions?: Record<string, unknown> | null;
}

/** The subscribe payload for a subscription: by id, with an empty query, when it has one. */
export function persistedSubscription<T extends SubscribePayload>(
  payload: T,
  ids: DocumentIds,
): T | (T & { id: string }) {
  const id = payload.operationName ? ids[payload.operationName] : undefined;
  return id ? { ...payload, query: "", id } : payload;
}

/** Leaves an empty `query` out of a graphql-ws message; pass it as `jsonMessageReplacer`. */
export function persistedMessageReplacer(key: string, value: unknown): unknown {
  return key === "query" && value === "" ? undefined : value;
}
