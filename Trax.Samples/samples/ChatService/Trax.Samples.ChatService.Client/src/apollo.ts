import {
  ApolloClient,
  ApolloLink,
  InMemoryCache,
  HttpLink,
  Observable,
  split,
} from "@apollo/client";
import { GraphQLWsLink } from "@apollo/client/link/subscriptions";
import { getMainDefinition } from "@apollo/client/utilities";
import { print } from "graphql";
import { createClient } from "graphql-ws";
import { maskKey, record } from "./inspector";

const API_URL = "http://localhost:5210/trax/graphql";
const WS_URL = "ws://localhost:5210/trax/graphql";

const pascal = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** The Trax field an operation calls: `dispatch { sendMessage }` runs the SendMessage train. */
function traxField(query: Parameters<typeof getMainDefinition>[0]): { namespace: string; field: string } | null {
  const def = getMainDefinition(query);
  const root = def.selectionSet.selections[0];
  if (root?.kind !== "Field") return null;
  const inner = root.selectionSet?.selections[0];
  return inner?.kind === "Field" ? { namespace: root.name.value, field: inner.name.value } : null;
}

/** Records every HTTP GraphQL request and its response for the inspector. */
function inspectorLink(user: string): ApolloLink {
  return new ApolloLink((operation, forward) => {
    const def = getMainDefinition(operation.query);
    const kind = def.kind === "OperationDefinition" ? def.operation : "query";
    const target = traxField(operation.query);
    const train = target ? pascal(target.field) : operation.operationName;
    // The room list polls every few seconds; it is real traffic, but background noise next to a chat.
    const quiet = operation.operationName === "GetChatRooms";
    const started = performance.now();

    record({
      channel: "http",
      direction: "out",
      user,
      quiet,
      title: `POST ${kind} ${operation.operationName}`,
      note:
        target?.namespace === "dispatch"
          ? `Asks Trax to run the ${train} train now (dispatch.${target.field}).`
          : target?.namespace === "discover"
            ? `Runs the ${train} query train (discover.${target.field}).`
            : undefined,
      detail: { query: print(operation.query), variables: operation.variables },
    });

    return new Observable((observer) => {
      const sub = forward(operation).subscribe({
        next: (result) => {
          const payload = target ? (result.data as Record<string, Record<string, { externalId?: string }>>)?.[target.namespace]?.[target.field] : undefined;
          const runId = payload?.externalId;
          record({
            channel: "http",
            direction: "in",
            user,
            quiet,
            runId,
            failed: !!result.errors?.length,
            durationMs: Math.round(performance.now() - started),
            title: `${result.errors?.length ? "Errors" : "200 OK"} ${operation.operationName}`,
            note: result.errors?.length
              ? result.errors.map((e) => e.message).join("; ")
              : runId
                ? `The ${train} train completed; Trax recorded the run as ${runId.slice(0, 8)}.`
                : undefined,
            detail: result,
          });
          observer.next(result);
        },
        error: (error) => {
          record({
            channel: "http",
            direction: "in",
            user,
            failed: true,
            durationMs: Math.round(performance.now() - started),
            title: `Failed ${operation.operationName}`,
            note: String(error?.message ?? error),
          });
          observer.error(error);
        },
        complete: () => observer.complete(),
      });
      return () => sub.unsubscribe();
    });
  });
}

interface Frame {
  type: string;
  id?: string;
  payload?: Record<string, unknown> & {
    data?: { onChatEvent?: { eventType?: string; trainExternalId?: string; chatRoomId?: string } };
    variables?: { chatRoomId?: string };
    query?: string;
  };
}

/** Records one graphql-ws frame, sent or received, with what it means on the Trax side. */
function recordFrame(user: string, direction: "out" | "in", raw: string) {
  let frame: Frame;
  try {
    frame = JSON.parse(raw);
  } catch {
    record({ channel: "ws", direction, user, title: "Unreadable frame", detail: raw });
    return;
  }

  const shown: Frame = structuredClone(frame);
  if (shown.type === "connection_init" && typeof shown.payload?.apiKey === "string")
    shown.payload.apiKey = maskKey(shown.payload.apiKey);

  const event = frame.payload?.data?.onChatEvent;
  const notes: Record<string, string> = {
    connection_init: "Sends the API key in the first frame: a browser cannot set headers on a WebSocket upgrade.",
    connection_ack: "Trax authenticated the socket from the key in connection_init.",
    subscribe: `Subscribes to onChatEvent for room ${String(frame.payload?.variables?.chatRoomId ?? "").slice(0, 8)}; Trax checks the caller is in the room.`,
    complete: "Ends the subscription.",
    error: "Trax refused the subscription.",
  };

  record({
    channel: "ws",
    direction,
    user,
    quiet: frame.type === "ping" || frame.type === "pong",
    title: event ? `next · ${event.eventType}` : frame.type,
    runId: event?.trainExternalId,
    failed: frame.type === "error",
    note: event
      ? `When the chat train finished, ChatLifecycleHook published ${event.eventType} to this room's topic; same run ${event.trainExternalId?.slice(0, 8)}.`
      : notes[frame.type],
    detail: shown,
  });
}

/** A WebSocket that reports its lifecycle and every frame to the inspector. */
function loggingWebSocket(user: string): typeof WebSocket {
  return class InspectedWebSocket extends WebSocket {
    constructor(url: string | URL, protocols?: string | string[]) {
      super(url, protocols);
      record({ channel: "ws", direction: "info", user, title: "Opening WebSocket", note: `${url} (graphql-transport-ws)` });
      this.addEventListener("open", () => record({ channel: "ws", direction: "info", user, title: "WebSocket open" }));
      this.addEventListener("message", (e) => recordFrame(user, "in", String(e.data)));
      this.addEventListener("close", (e) =>
        record({
          channel: "ws",
          direction: "info",
          user,
          failed: e.code !== 1000,
          title: `WebSocket closed (${e.code})`,
          note: e.code === 4403 ? "Trax closed the socket: no valid API key in connection_init." : e.reason || undefined,
        }),
      );
    }

    send(data: string | ArrayBufferLike | Blob | ArrayBufferView) {
      if (typeof data === "string") recordFrame(user, "out", data);
      super.send(data);
    }
  };
}

export function createApolloClient(apiKey: string, displayName: string): ApolloClient<unknown> {
  const httpLink = inspectorLink(displayName).concat(
    new HttpLink({
      uri: API_URL,
      headers: { "X-Api-Key": apiKey },
    }),
  );

  const wsLink = new GraphQLWsLink(
    createClient({
      url: WS_URL,
      webSocketImpl: loggingWebSocket(displayName),
      // Browsers cannot set headers on a WebSocket upgrade, so the key travels in the
      // connection_init payload. Trax reads it from "apiKey" (or "authToken").
      connectionParams: { apiKey },
    }),
  );

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

  return new ApolloClient({
    link,
    cache: new InMemoryCache(),
  });
}
