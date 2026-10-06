import type { TrainState } from "../types";

const STYLES: Record<TrainState, string> = {
  PENDING: "bg-raised text-fg-2",
  IN_PROGRESS: "bg-info-soft text-info-fg",
  COMPLETED: "bg-ok-soft text-ok-fg",
  FAILED: "bg-danger-soft text-danger-fg",
  CANCELLED: "bg-warn-soft text-warn-fg",
};

const LABELS: Record<TrainState, string> = {
  PENDING: "Pending",
  IN_PROGRESS: "In progress",
  COMPLETED: "Completed",
  FAILED: "Failed",
  CANCELLED: "Cancelled",
};

export function StateBadge({ state }: { state: TrainState }) {
  return (
    <span
      className={`inline-block text-xs font-medium px-2 py-0.5 rounded-full ${STYLES[state]}`}
    >
      {LABELS[state]}
    </span>
  );
}
