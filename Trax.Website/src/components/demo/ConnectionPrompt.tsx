"use client";

import { useState } from "react";

interface ConnectionPromptProps {
  onConnect: (url: string, apiKey: string) => void;
}

export default function ConnectionPrompt({ onConnect }: ConnectionPromptProps) {
  const [url, setUrl] = useState(
    process.env.NEXT_PUBLIC_GRAPHQL_URL || "http://localhost:5200/trax/graphql"
  );
  const [apiKey, setApiKey] = useState(
    process.env.NEXT_PUBLIC_GRAPHQL_API_KEY || ""
  );

  return (
    <div className="rounded-lg border border-border bg-bg-card p-8">
      <h2 className="text-xl font-semibold text-text-primary">
        Connect to a Trax Server
      </h2>
      <p className="mt-2 text-sm text-text-secondary">
        Enter the URL of a running Trax GraphQL server to explore live data.
      </p>

      <div className="mt-6 space-y-4">
        <div>
          <label
            htmlFor="graphql-url"
            className="block text-sm font-medium text-text-secondary"
          >
            GraphQL URL
          </label>
          <input
            id="graphql-url"
            type="url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            className="mt-1 w-full rounded-lg border border-border bg-bg-secondary px-3 py-2 text-sm text-text-primary placeholder-text-muted focus:border-accent focus:outline-none"
            placeholder="http://localhost:5200/trax/graphql"
          />
        </div>
        <div>
          <label
            htmlFor="api-key"
            className="block text-sm font-medium text-text-secondary"
          >
            API Key (optional)
          </label>
          <input
            id="api-key"
            type="password"
            value={apiKey}
            onChange={(e) => setApiKey(e.target.value)}
            className="mt-1 w-full rounded-lg border border-border bg-bg-secondary px-3 py-2 text-sm text-text-primary placeholder-text-muted focus:border-accent focus:outline-none"
            placeholder="your-api-key"
          />
        </div>
        <button
          onClick={() => onConnect(url, apiKey)}
          className="rounded-lg bg-accent px-6 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
        >
          Connect
        </button>
      </div>

      <div className="mt-8 rounded-lg border border-border bg-bg-secondary p-4">
        <h3 className="text-sm font-medium text-text-primary">
          Running locally?
        </h3>
        <p className="mt-1 text-xs text-text-muted">
          Start the sample Trax server:
        </p>
        <pre className="mt-2 overflow-x-auto text-xs text-text-muted">
{`cd Trax.Samples && docker compose up -d
dotnet run --project samples/LocalWorkers/\\
  Trax.Samples.GameServer.Api`}
        </pre>
      </div>
    </div>
  );
}
