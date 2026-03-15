"use client";

import { useQuery } from "@apollo/client/react";
import { TRAINS_QUERY } from "@/lib/graphql/queries";
import type { TrainsData } from "@/lib/graphql/types";

export default function TrainRegistry() {
  const { data, loading, error } = useQuery<TrainsData>(TRAINS_QUERY);

  if (loading) {
    return (
      <div className="rounded-lg border border-border bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-text-muted">
          Registered Trains
        </h3>
        <p className="mt-2 text-sm text-text-muted">Loading...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-lg border border-error/30 bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-error">Registered Trains</h3>
        <p className="mt-2 text-sm text-text-muted">{error.message}</p>
      </div>
    );
  }

  const trains = data?.operations?.trains || [];

  return (
    <div className="rounded-lg border border-border bg-bg-card p-6">
      <h3 className="text-sm font-semibold text-text-muted">
        Registered Trains ({trains.length})
      </h3>
      <div className="mt-4 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-border">
              <th className="pb-2 pr-4 font-medium text-text-muted">Name</th>
              <th className="pb-2 pr-4 font-medium text-text-muted">Input</th>
              <th className="pb-2 pr-4 font-medium text-text-muted">Output</th>
              <th className="pb-2 font-medium text-text-muted">Type</th>
            </tr>
          </thead>
          <tbody>
            {trains.map((train, i) => {
                const shortName =
                  train.serviceTypeName.split(".").pop() || train.serviceTypeName;
                const shortInput =
                  train.inputTypeName?.split(".").pop() || "-";
                const shortOutput =
                  train.outputTypeName?.split(".").pop() || "-";
                const type = train.isQuery
                  ? "Query"
                  : train.isMutation
                    ? "Mutation"
                    : "Train";

                return (
                  <tr
                    key={i}
                    className="border-b border-border last:border-0"
                  >
                    <td className="py-2 pr-4 font-mono text-xs text-text-primary">
                      {shortName}
                    </td>
                    <td className="py-2 pr-4 font-mono text-xs text-text-muted">
                      {shortInput}
                    </td>
                    <td className="py-2 pr-4 font-mono text-xs text-text-muted">
                      {shortOutput}
                    </td>
                    <td className="py-2">
                      <span
                        className={`rounded px-1.5 py-0.5 text-xs ${
                          type === "Query"
                            ? "bg-info/10 text-info"
                            : type === "Mutation"
                              ? "bg-warning/10 text-warning"
                              : "bg-accent-muted text-accent"
                        }`}
                      >
                        {type}
                      </span>
                    </td>
                  </tr>
                );
              }
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
