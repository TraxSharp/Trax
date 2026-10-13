import {
  ApolloClient,
  ApolloLink,
  HttpLink,
  InMemoryCache,
  Observable,
  split,
  type FetchResult,
  type Operation,
} from "@apollo/client";
import { getMainDefinition } from "@apollo/client/utilities";
import { createClient, type Client, type SubscribePayload } from "graphql-ws";
import { PERSISTED } from "./graphql";

export const API_URL = "http://localhost:5260/trax/graphql";
export const WS_URL = "ws://localhost:5260/trax/graphql";

// The operator demo key, registered by the host in Development only. The page needs each
// question's answer and the names of the junctions on a track, which only the operations view of
// onJunctionEvent carries; a key without the Operator role would see them withheld.
export const API_KEY = "recovery-operator-key-do-not-use-in-production";

// The host runs only persisted documents, so every operation goes by the id persisted-operations.json gives
// it, with its variables, and never its text.
const persistedIdOf = (operationName: string | undefined): string => {
  const persisted = operationName ? PERSISTED.get(operationName) : undefined;
  if (!persisted)
    throw new Error(`The operation ${operationName ?? "(unnamed)"} is not in persisted-operations.json.`);
  return persisted.id;
};

const persistedFetch: typeof fetch = (input, init) => {
  const body = JSON.parse(String(init?.body)) as { operationName?: string; variables?: unknown };
  return fetch(input, {
    ...init,
    body: JSON.stringify({
      id: persistedIdOf(body.operationName),
      operationName: body.operationName,
      variables: body.variables,
    }),
  });
};

const httpLink = new HttpLink({ uri: API_URL, headers: { "X-Api-Key": API_KEY }, fetch: persistedFetch });

// A subscription over the socket, by its persisted id. Browsers cannot set headers on a WebSocket
// upgrade: the key travels in connection_init, under `apiKey` (or `authToken`).
class PersistedSocketLink extends ApolloLink {
  constructor(private readonly socket: Client) {
    super();
  }

  public override request(operation: Operation): Observable<FetchResult> {
    return new Observable<FetchResult>((sink) => {
      const payload = {
        query: "",
        id: persistedIdOf(operation.operationName),
        operationName: operation.operationName,
        variables: operation.variables,
      } as unknown as SubscribePayload;
      return this.socket.subscribe(payload, {
        next: (value) => sink.next(value as FetchResult),
        complete: () => sink.complete(),
        error: (error) => sink.error(error instanceof Error ? error : new Error(JSON.stringify(error))),
      });
    });
  }
}

// graphql-ws requires a subscribe message to carry a document, and the host refuses one alongside a persisted
// id: the link sends an empty one, and the replacer leaves it out of the message.
export const persistedSocket = (): Client =>
  createClient({
    url: WS_URL,
    connectionParams: { apiKey: API_KEY },
    lazy: true,
    jsonMessageReplacer: (key, value) => (key === "query" && value === "" ? undefined : value),
  });

/** Subscribes on `socket` to the persisted subscription `operationName`. Returns what stops it. */
export function subscribePersisted<T>(
  socket: Client,
  operationName: string,
  variables: Record<string, unknown>,
  next: (data: T) => void,
): () => void {
  const payload = { query: "", id: persistedIdOf(operationName), operationName, variables } as unknown as SubscribePayload;
  return socket.subscribe(payload, {
    next: (value) => {
      if (value.data) next(value.data as T);
    },
    error: () => undefined,
    complete: () => undefined,
  });
}

const wsLink = new PersistedSocketLink(persistedSocket());

const link = split(
  ({ query }) => {
    const definition = getMainDefinition(query);
    return (
      definition.kind === "OperationDefinition" &&
      definition.operation === "subscription"
    );
  },
  wsLink,
  httpLink,
);

export const client = new ApolloClient({
  link,
  cache: new InMemoryCache(),
  defaultOptions: {
    query: { fetchPolicy: "no-cache" },
    mutate: { fetchPolicy: "no-cache" },
  },
});
