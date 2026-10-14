import {
  Client,
  cacheExchange,
  fetchExchange,
  subscriptionExchange,
} from "@urql/core";
import { createClient as createWsClient } from "graphql-ws";
import { authConnectionParams, authHttpHeaders, onAuthChange } from "./auth";
import { activityExchange } from "./activity-exchange";
import { connection, setConnectionStatus, type ConnectionStatus } from "./connection";
import { shouldRetryWs } from "./wsRetry";

// Re-exported for existing importers (useConnection, ConnectionIndicator).
export { connection };
export type { ConnectionStatus };

// Vite proxies /trax (HTTP + WebSocket) to the API host, so both are same-origin here.
const HTTP_URL = "/trax/graphql";
const WS_URL = `${
  window.location.protocol === "https:" ? "wss" : "ws"
}://${window.location.host}/trax/graphql`;

// Browsers cannot set headers on a WebSocket upgrade, so the API reads the credential from the
// graphql-ws connection_init payload — `apiKey` for API-key mode, `authToken` for Bearer/JWT mode
// (see lib/auth). connectionParams is a function so each (re)connect picks up the current value.
//
// retryAttempts: Infinity makes the socket self-healing — it keeps reconnecting (randomised
// exponential backoff, capped) through server restarts and network drops instead of giving up
// after graphql-ws's default 5 tries. shouldRetryWs stops the loop only on auth rejections, which
// a retry can't fix (re-auth terminates and reconnects the socket out of band). Active
// subscriptions are re-established automatically on each reconnect; pages fill the gap with
// useRefetchOnReconnect, since graphql-ws does not replay events missed while disconnected.
const wsClient = createWsClient({
  url: WS_URL,
  connectionParams: () => authConnectionParams(),
  retryAttempts: Infinity,
  shouldRetry: shouldRetryWs,
  on: {
    connecting: () => setConnectionStatus("connecting"),
    connected: () => setConnectionStatus("connected"),
    closed: () => setConnectionStatus("closed"),
  },
});

// When the credential changes, drop the socket so it reconnects with the new one.
onAuthChange(() => wsClient.terminate());

export const client = new Client({
  url: HTTP_URL,
  // Default every query to revalidate on navigation. Operations mutations return a generic
  // OperationResponse (no entity __typename), so urql's document cache can't tell which list
  // queries a mutation invalidated. cache-and-network renders cached rows instantly, then
  // refreshes from the network, so a change made on one page shows up on every other.
  requestPolicy: "cache-and-network",
  // Trax serves GraphQL over POST only (AllowGetRequests() is an opt-in, off by default, so a
  // cross-site link cannot run a query as the signed-in user). urql 6 sends queries as GET by
  // default, which Trax answers 404, so pin every operation to POST.
  preferGetMethod: false,
  exchanges: [
    activityExchange,
    cacheExchange,
    fetchExchange,
    subscriptionExchange({
      forwardSubscription(request) {
        const input = { ...request, query: request.query ?? "" };
        return {
          subscribe(sink) {
            const unsubscribe = wsClient.subscribe(input, sink);
            return { unsubscribe };
          },
        };
      },
    }),
  ],
  // Read the credential per request so it stays current after ConnectionGate updates it.
  fetchOptions: () => ({ headers: authHttpHeaders() }),
});
