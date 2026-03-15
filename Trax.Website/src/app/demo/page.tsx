"use client";

import { useState } from "react";
import { ApolloProvider } from "@apollo/client/react";
import { createApolloClient } from "@/lib/graphql/client";
import ConnectionPrompt from "@/components/demo/ConnectionPrompt";
import HealthWidget from "@/components/demo/HealthWidget";
import TrainRegistry from "@/components/demo/TrainRegistry";
import ExecutionFeed from "@/components/demo/ExecutionFeed";
import ManifestBrowser from "@/components/demo/ManifestBrowser";

export default function DemoPage() {
  const [client, setClient] = useState<ReturnType<
    typeof createApolloClient
  > | null>(null);
  const [connected, setConnected] = useState(false);

  function handleConnect(url: string, apiKey: string) {
    const apolloClient = createApolloClient(url, apiKey);
    setClient(apolloClient);
    setConnected(true);
  }

  function handleDisconnect() {
    if (client) {
      client.stop();
    }
    setClient(null);
    setConnected(false);
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-text-primary">Live Demo</h1>
          <p className="mt-2 text-text-secondary">
            Connect to a Trax GraphQL server to explore real-time train
            execution data.
          </p>
        </div>
        {connected && (
          <button
            onClick={handleDisconnect}
            className="rounded-lg border border-border px-4 py-2 text-sm text-text-secondary transition-colors hover:border-error hover:text-error"
          >
            Disconnect
          </button>
        )}
      </div>

      <div className="mt-8">
        {!connected || !client ? (
          <ConnectionPrompt onConnect={handleConnect} />
        ) : (
          <ApolloProvider client={client}>
            <div className="space-y-6">
              <HealthWidget />
              <div className="grid gap-6 lg:grid-cols-2">
                <TrainRegistry />
                <ExecutionFeed />
              </div>
              <ManifestBrowser />
            </div>
          </ApolloProvider>
        )}
      </div>
    </div>
  );
}
