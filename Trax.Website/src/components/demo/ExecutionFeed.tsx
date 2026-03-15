"use client";

import { useSubscription } from "@apollo/client/react";
import { useState } from "react";
import { ON_TRAIN_STATE_CHANGED } from "@/lib/graphql/subscriptions";
import type { TrainEvent, TrainStateChangedData } from "@/lib/graphql/types";

const stateColors: Record<string, string> = {
  Queued: "text-text-muted",
  Running: "text-info",
  Completed: "text-success",
  Failed: "text-error",
  Cancelled: "text-warning",
  DeadLettered: "text-error",
};

export default function ExecutionFeed() {
  const [events, setEvents] = useState<TrainEvent[]>([]);

  const { error } = useSubscription<TrainStateChangedData>(ON_TRAIN_STATE_CHANGED, {
    onData: ({ data: subData }) => {
      const event = subData.data?.onTrainStateChanged;
      if (event) {
        setEvents((prev) => [event, ...prev].slice(0, 50));
      }
    },
  });

  return (
    <div className="rounded-lg border border-border bg-bg-card p-6">
      <h3 className="text-sm font-semibold text-text-muted">
        Live Event Feed
      </h3>

      {error && (
        <p className="mt-2 text-xs text-error">
          Subscription error: {error.message}
        </p>
      )}

      <div className="mt-4 max-h-80 space-y-2 overflow-y-auto">
        {events.length === 0 ? (
          <p className="text-xs text-text-muted">
            Waiting for train events... Run a train on the connected server to
            see live updates.
          </p>
        ) : (
          events.map((event, i) => {
            const shortName =
              event.trainName.split(".").pop() || event.trainName;
            const color = stateColors[event.trainState] || "text-text-muted";
            const time = new Date(event.timestamp).toLocaleTimeString();

            return (
              <div
                key={`${event.metadataId}-${event.trainState}-${i}`}
                className="flex items-center gap-3 rounded border border-border bg-bg-secondary px-3 py-2 text-xs"
              >
                <span className="text-text-muted">{time}</span>
                <span className="font-mono text-text-primary">
                  {shortName}
                </span>
                <span className={`font-medium ${color}`}>
                  {event.trainState}
                </span>
                {event.failureReason && (
                  <span className="truncate text-error">
                    {event.failureReason}
                  </span>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
