import { useMemo } from "react";
import { useQuery } from "urql";
import { ENVIRONMENT_NAME } from "../graphql/queries";
import { usePollSeconds } from "../lib/poll";
import { useRefreshStatus } from "../lib/refreshStatus";
import { useNow } from "../lib/useNow";
import { environmentBadgeColor } from "../lib/environment";
import { Clock, RotateCw, TriangleAlert } from "lucide-react";

/**
 * The top bar, as the Blazor DashboardHeader: the host's environment badge, a "Last refresh
 * failed" mark while the latest query is failing, a countdown to the next auto-refresh (when one
 * is set in User settings), and the current UTC time.
 */
export function DashboardHeader() {
  const context = useMemo(() => ({ ignoreRefreshStatus: true }), []);
  const [{ data }] = useQuery<{ operations: { config: { environmentName: string } } | null }>({
    query: ENVIRONMENT_NAME,
    context,
  });
  const environment = data?.operations?.config?.environmentName ?? "";
  const { lastRefreshAt, lastError } = useRefreshStatus();
  const pollSeconds = usePollSeconds();
  const now = useNow(500);

  const elapsed = lastRefreshAt == null ? 0 : (now - lastRefreshAt) / 1000;
  const remaining = Math.max(0, Math.ceil(pollSeconds - elapsed));
  const progress = pollSeconds > 0 ? Math.min(1, Math.max(0, elapsed / pollSeconds)) : 0;
  const utc = new Date(now).toISOString().replace("T", " ").slice(0, 19);

  return (
    <header className="flex items-center gap-4 px-8 py-2 border-b border-line bg-surface text-xs text-muted">
      {environment && (
        <span
          data-testid="environment-badge"
          className="px-2 py-0.5 rounded font-semibold text-white"
          style={{ backgroundColor: environmentBadgeColor(environment) }}
        >
          Environment: {environment}
        </span>
      )}
      <div className="ml-auto flex items-center gap-4">
        {lastError && (
          <span
            role="status"
            title={lastError}
            className="flex items-center gap-1 text-danger-fg"
          >
            <TriangleAlert className="size-3.5" />
            Last refresh failed
          </span>
        )}
        {pollSeconds > 0 ? (
          <span
            className="flex items-center gap-2"
            title="Time until next data refresh"
            aria-label="Next refresh"
          >
            <RotateCw className="size-3.5" />
            <span className="tabular-nums min-w-[1.6rem] text-right">{remaining}s</span>
            <span className="w-14 h-1 rounded bg-raised overflow-hidden">
              <span
                className="block h-full bg-accent"
                style={{ width: `${(progress * 100).toFixed(0)}%` }}
              />
            </span>
          </span>
        ) : (
          <span title="Lists update live over the subscription; set an auto-refresh interval in User settings">
            Auto-refresh off
          </span>
        )}
        <span
          className="flex items-center gap-1.5 tabular-nums"
          title="Current UTC time"
          data-testid="utc-clock"
        >
          <Clock className="size-3.5" />
          {utc} UTC
        </span>
      </div>
    </header>
  );
}
