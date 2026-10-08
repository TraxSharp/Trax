import { ApolloClient, HttpLink, InMemoryCache, split } from "@apollo/client";
import { GraphQLWsLink } from "@apollo/client/link/subscriptions";
import { getMainDefinition } from "@apollo/client/utilities";
import { createClient } from "graphql-ws";

export const API_URL = "http://localhost:5260/trax/graphql";
const WS_URL = "ws://localhost:5260/trax/graphql";

// The operator demo key, registered by the host in Development only. The page needs each
// question's answer and the names of the junctions on a track, which only the operations view of
// onJunctionEvent carries; a key without the Operator role would see them withheld.
export const API_KEY = "recovery-operator-key-do-not-use-in-production";

const httpLink = new HttpLink({ uri: API_URL, headers: { "X-Api-Key": API_KEY } });

// Browsers cannot set headers on a WebSocket upgrade: the key travels in connection_init, under
// `apiKey` (or `authToken`).
const wsLink = new GraphQLWsLink(
  createClient({ url: WS_URL, connectionParams: { apiKey: API_KEY } }),
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

export const client = new ApolloClient({
  link,
  cache: new InMemoryCache(),
  defaultOptions: {
    query: { fetchPolicy: "no-cache" },
    mutate: { fetchPolicy: "no-cache" },
  },
});
