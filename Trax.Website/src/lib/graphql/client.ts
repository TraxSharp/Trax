"use client";

import {
  ApolloClient,
  InMemoryCache,
  HttpLink,
  split,
} from "@apollo/client/core";
import { GraphQLWsLink } from "@apollo/client/link/subscriptions";
import { getMainDefinition } from "@apollo/client/utilities";
import { createClient } from "graphql-ws";

export function createApolloClient(url: string, apiKey?: string) {
  const headers: Record<string, string> = {};
  if (apiKey) {
    headers["X-Api-Key"] = apiKey;
  }

  const httpLink = new HttpLink({
    uri: url,
    headers,
  });

  // WebSocket link for subscriptions
  const wsUrl = url.replace(/^http/, "ws");
  const wsLink = new GraphQLWsLink(
    createClient({
      url: wsUrl,
      connectionParams: () => headers,
    })
  );

  // Route subscriptions through WebSocket, everything else through HTTP
  const splitLink = split(
    ({ query }) => {
      const definition = getMainDefinition(query);
      return (
        definition.kind === "OperationDefinition" &&
        definition.operation === "subscription"
      );
    },
    wsLink,
    httpLink
  );

  return new ApolloClient({
    link: splitLink,
    cache: new InMemoryCache(),
  });
}
