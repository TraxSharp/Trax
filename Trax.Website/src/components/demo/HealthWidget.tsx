"use client";

import { useQuery } from "@apollo/client/react";
import { HEALTH_QUERY } from "@/lib/graphql/queries";
import type { HealthData } from "@/lib/graphql/types";

export default function HealthWidget() {
  const { data, loading, error } = useQuery<HealthData>(HEALTH_QUERY, {
    pollInterval: 5000,
  });

  if (loading) {
    return (
      <div className="rounded-lg border border-border bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-text-muted">Health</h3>
        <p className="mt-2 text-sm text-text-muted">Loading...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="rounded-lg border border-error/30 bg-bg-card p-6">
        <h3 className="text-sm font-semibold text-error">Health</h3>
        <p className="mt-2 text-sm text-text-muted">
          Failed to fetch: {error.message}
        </p>
      </div>
    );
  }

  const health = data?.operations?.health;
  if (!health) return null;

  const stats = [
    { label: "Status", value: health.status, color: "text-success" },
    { label: "Queue Depth", value: health.queueDepth, color: "text-info" },
    { label: "In Progress", value: health.inProgress, color: "text-accent" },
    {
      label: "Failed (1h)",
      value: health.failedLastHour,
      color: health.failedLastHour > 0 ? "text-warning" : "text-text-muted",
    },
    {
      label: "Dead Letters",
      value: health.deadLetters,
      color: health.deadLetters > 0 ? "text-error" : "text-text-muted",
    },
  ];

  return (
    <div className="rounded-lg border border-border bg-bg-card p-6">
      <h3 className="text-sm font-semibold text-text-muted">Health</h3>
      <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-5">
        {stats.map((stat) => (
          <div key={stat.label}>
            <div className="text-xs text-text-muted">{stat.label}</div>
            <div className={`mt-1 text-lg font-semibold ${stat.color}`}>
              {stat.value}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
