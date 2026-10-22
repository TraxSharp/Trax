import { httpTransport } from "../trax/index.g";
import { authConnectionParams, authHttpHeaders, onAuthChange } from "./auth";
import { connection, setConnectionStatus, type ConnectionStatus } from "./connection";
import { ConsoleClient } from "./consoleClient";
import { shouldRetryWs } from "./wsRetry";
import { graphqlUrls, readRuntimeConfig } from "./runtimeConfig";

// Re-exported for existing importers (useConnection, ConnectionIndicator).
export { connection };
export type { ConnectionStatus };

// The endpoint is always on this page's origin: the host that serves the app names its path in
// index.html (lib/runtimeConfig), and in development Vite proxies /trax (HTTP + WebSocket) to the
// API host. Same-origin, so CORS is never involved.
const config = readRuntimeConfig();
const { http: HTTP_URL, ws: WS_URL } = graphqlUrls(config.graphqlPath);

// A host that runs persisted operations registers the console's documents and names the id of each
// one it serves; every request for one of those goes by that id. A document it does not name (its
// host skipped it, or runs no persisted operations), and persisted-operation management, which no
// host registers, goes as text.
const DOCUMENT_IDS = config.documents;

// Trax serves GraphQL over POST only (AllowGetRequests() is an opt-in, off by default, so a
// cross-site link cannot run a query as the signed-in user); the transport sends every request as POST.
//
// Browsers cannot set headers on a WebSocket upgrade, so the API reads the credential from the
// graphql-ws connection_init payload: `apiKey` for API-key mode, `authToken` for Bearer/JWT mode
// (see lib/auth). connectionParams is a function so each (re)connect picks up the current value.
//
// retryAttempts: Infinity makes the socket self-healing: it keeps reconnecting (randomised
// exponential backoff, capped) through server restarts and network drops instead of giving up
// after graphql-ws's default 5 tries. shouldRetryWs stops the loop only on auth rejections, which
// a retry can't fix (re-auth terminates and reconnects the socket out of band). Active
// streams are re-established automatically on each reconnect; pages fill the gap with
// useRefetchOnReconnect, since graphql-ws does not replay events missed while disconnected.
const transport = httpTransport({
  url: HTTP_URL,
  socketUrl: WS_URL,
  // Read the credential per request so it stays current after ConnectionGate updates it.
  headers: () => authHttpHeaders(),
  connectionParams: () => authConnectionParams(),
  byId: (document) => document.id !== null && DOCUMENT_IDS[document.name] === document.id,
  socket: {
    retryAttempts: Infinity,
    shouldRetry: shouldRetryWs,
    on: {
      connecting: () => setConnectionStatus("connecting"),
      connected: () => setConnectionStatus("connected"),
      closed: () => setConnectionStatus("closed"),
    },
  },
});

// When the credential changes, drop the socket so it reconnects with the new one.
onAuthChange(() => transport.terminate());

export const client = new ConsoleClient(transport);
