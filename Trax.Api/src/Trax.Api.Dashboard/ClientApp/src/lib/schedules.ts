import { useQuery } from "urql";
import { GROUP_GRAPH_STATUS } from "../graphql/queries";
import { useAnswers } from "./answerable";
import type { GroupStatus } from "./groupGraph";
import type { ManifestGroupDependencyGraph } from "../types";

// Times as the Schedules area words them, and the status a group dependency graph is toned by.

/** "in 4 min", "in 2 h 05 min", "in 3 days", or "due now" for a time that has come. */
export function untilText(iso: string, now: number): string {
  const ms = Date.parse(iso) - now;
  if (!Number.isFinite(ms) || ms <= 30_000) return "due now";
  const minutes = Math.round(ms / 60_000);
  if (minutes < 1) return "in under a minute";
  if (minutes < 60) return `in ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `in ${hours} h ${String(minutes % 60).padStart(2, "0")} min`;
  return `in ${Math.round(hours / 24)} days`;
}

/** A time in UTC, as the schedule text reads it: "Tue 7 Jul, 12:05 UTC". */
export function utcText(iso: string): string {
  return `${new Date(iso).toLocaleString("en-GB", {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "UTC",
  })} UTC`;
}

/** The API reads at most this many group ids in one stats call. */
const MAX_STATUS_IDS = 1000;
const NO_STATUSES: GroupStatus[] = [];

/**
 * Whether each group of a dependency graph is running or failing, in one read. Empty where the
 * client cannot answer it, and the graph's nodes are then toned by enabled alone.
 */
export function useGroupGraphStatus(graph: ManifestGroupDependencyGraph | null | undefined): {
  statuses: GroupStatus[];
  refetch: () => void;
} {
  const answered = useAnswers("GroupGraphStatus");
  const groupIds = (graph?.nodes ?? []).slice(0, MAX_STATUS_IDS).map((n) => n.id);
  const [{ data }, reexecute] = useQuery<{
    operations: { manifestGroups: { stats: GroupStatus[] } };
  }>({
    query: GROUP_GRAPH_STATUS,
    variables: { groupIds },
    pause: !answered || groupIds.length === 0,
  });
  return {
    statuses: data?.operations?.manifestGroups?.stats ?? NO_STATUSES,
    refetch: () => {
      if (answered && groupIds.length > 0) reexecute({ requestPolicy: "network-only" });
    },
  };
}
