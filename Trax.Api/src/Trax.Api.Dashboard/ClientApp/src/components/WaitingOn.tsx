import { Link } from "react-router-dom";
import { StateBadge } from "./StateBadge";
import type { MachineInstanceWaitingRun } from "../types";

/** The run an instance's state waits on: its run page and state, or its queue entry while queued. */
export function WaitingOn({ run: w }: { run: MachineInstanceWaitingRun | null }) {
  if (!w) return null;
  return (
    <span className="flex flex-wrap items-center gap-1">
      {w.executionId != null ? (
        <Link to={`/runs/${w.executionId}`} className="text-accent-fg hover:underline">
          Run {w.executionId}
        </Link>
      ) : w.workQueueEntryId != null ? (
        <Link to={`/runs/queued/${w.workQueueEntryId}`} className="text-accent-fg hover:underline">
          Queue entry {w.workQueueEntryId}
        </Link>
      ) : (
        <span className="text-muted">A run not found</span>
      )}
      {w.trainState && <StateBadge state={w.trainState} />}
    </span>
  );
}
