import {
  Client,
  cacheExchange,
  fetchExchange,
  subscriptionExchange,
} from "@urql/core";
import { createClient as createWsClient } from "graphql-ws";

const wsClient = createWsClient({
  url: `ws://${window.location.host}/trax/graphql`,
});

export const client = new Client({
  url: "/trax/graphql",
  exchanges: [
    cacheExchange,
    fetchExchange,
    subscriptionExchange({
      forwardSubscription(request) {
        const input = { ...request, query: request.query || "" };
        return {
          subscribe(sink) {
            const unsubscribe = wsClient.subscribe(input, sink);
            return { unsubscribe };
          },
        };
      },
    }),
  ],
  fetchOptions: () => {
    const apiKey = localStorage.getItem("jobhunt-api-key") || "alice-key";
    return { headers: { "X-Api-Key": apiKey } };
  },
});
