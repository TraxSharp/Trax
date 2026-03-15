"use client";

import { useQuery } from "@apollo/client/react";
import { MANIFESTS_QUERY } from "@/lib/graphql/queries";
import type { ManifestsData } from "@/lib/graphql/types";

export default function ManifestBrowser() {
  const { data, loading, error } = useQuery<ManifestsData>(MANIFESTS_QUERY, {
    variables: { skip: 0, take: 20 },
  });

  if (loading) {
    return (
      <div className="rounded-lg border border-border bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-text-muted">Manifests</h3>
        <p className="mt-2 text-sm text-text-muted">Loading...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-lg border border-error/30 bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-error">Manifests</h3>
        <p className="mt-2 text-sm text-text-muted">{error.message}</p>
      </div>
    );
  }

  const manifests = data?.operations?.manifests?.items || [];
  const totalCount = data?.operations?.manifests?.totalCount || 0;

  return (
    <div className="rounded-lg border border-border bg-bg-card p-6">
      <h3 className="text-sm font-semibold text-text-muted">
        Manifests ({totalCount})
      </h3>
      <div className="mt-4 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-border">
              <th className="pb-2 pr-4 font-medium text-text-muted">Name</th>
              <th className="pb-2 pr-4 font-medium text-text-muted">
                Schedule
              </th>
              <th className="pb-2 pr-4 font-medium text-text-muted">
                Enabled
              </th>
              <th className="pb-2 font-medium text-text-muted">Priority</th>
            </tr>
          </thead>
          <tbody>
            {manifests.map((manifest) => {
                const shortName =
                  manifest.name.split(".").pop() || manifest.name;
                const schedule =
                  manifest.cronExpression ||
                  (manifest.intervalSeconds
                    ? `Every ${manifest.intervalSeconds}s`
                    : manifest.scheduleType);

                return (
                  <tr
                    key={manifest.id}
                    className="border-b border-border last:border-0"
                  >
                    <td className="py-2 pr-4 font-mono text-xs text-text-primary">
                      {shortName}
                    </td>
                    <td className="py-2 pr-4 font-mono text-xs text-text-muted">
                      {schedule}
                    </td>
                    <td className="py-2 pr-4">
                      <span
                        className={`text-xs ${manifest.isEnabled ? "text-success" : "text-text-muted"}`}
                      >
                        {manifest.isEnabled ? "Yes" : "No"}
                      </span>
                    </td>
                    <td className="py-2 text-xs text-text-muted">
                      {manifest.priority}
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
