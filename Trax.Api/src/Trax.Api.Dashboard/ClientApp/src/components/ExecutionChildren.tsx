import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { EXECUTION_CHILDREN } from "../graphql/queries";
import { StateBadge } from "./StateBadge";
import { DetailPanel } from "./detail";
import type { ExecutionSummary, PagedResult } from "../types";

interface ChildrenData {
  operations: { executionChildren: PagedResult<ExecutionSummary> };
}

// Child executions of a parent. Only mounted when the parent reports childCount > 0, so the
// query never runs for the common (leaf) case.
export function ExecutionChildren({
  parentId,
  childCount,
}: {
  parentId: number;
  childCount: number;
}) {
  const [result] = useQuery<ChildrenData>({
    query: EXECUTION_CHILDREN,
    variables: { parentId, take: 25 },
  });

  const page = result.data?.operations.executionChildren;
  const items = page?.items ?? [];

  return (
    <DetailPanel title={`Child executions (${childCount})`}>
      {result.error ? (
        <p className="text-sm text-danger-fg">{result.error.message}</p>
      ) : items.length === 0 ? (
        <p className="text-sm text-muted">
          {result.fetching ? "Loading…" : "No child executions."}
        </p>
      ) : (
        <ul className="space-y-1 text-sm">
          {items.map((c) => (
            <li key={c.id} className="flex items-center gap-3">
              <span className="text-faint">└</span>
              <Link
                to={`/executions/${c.id}`}
                className="text-accent-fg hover:underline font-medium"
              >
                {shortName(c.name)}
              </Link>
              <StateBadge state={c.trainState} />
              <span className="text-muted ml-auto">
                {new Date(c.startTime).toLocaleTimeString()}
              </span>
            </li>
          ))}
          {page && items.length < childCount && (
            <li className="text-xs text-muted pl-6 pt-1">
              Showing first {items.length} of {childCount}.
            </li>
          )}
        </ul>
      )}
    </DetailPanel>
  );
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
