import { useEffect } from "react";
import { Link } from "react-router-dom";
import { usePollSeconds } from "../lib/poll";
import { useRefreshStatus } from "../lib/refreshStatus";
import { useNow } from "../lib/useNow";
import { ENVIRONMENT_BADGE, environmentTone } from "../lib/environment";
import { schedulerPaused } from "../lib/scheduler";
import { drainMark, drainRecheckDelay } from "../lib/drain";
import { useAnswers } from "../lib/answerable";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { Search } from "./Search";
import { CirclePause, CircleStop, Clock, RotateCw, TriangleAlert } from "lucide-react";
import { useTraxQuery } from "../lib/consoleClient";
import { drainDocument, environmentNameDocument, schedulerConfigDocument } from "../trax/index.g";

/**
 * The top bar, as the Blazor DashboardHeader: the host's environment badge, a "Last refresh
 * failed" mark while the latest query is failing, a countdown to the next auto-refresh (when one
 * is set in User settings), and the current UTC time.
 */
export function DashboardHeader() {
  const [{ data }] = useTraxQuery(environmentNameDocument, { ignoreRefreshStatus: true });
  const environment = data?.operations?.config?.environmentName ?? "";
  // Pausing the scheduler stops every schedule and every dispatch, so it shows on every page.
  const [{ data: scheduler }] = useTraxQuery(schedulerConfigDocument, { ignoreRefreshStatus: true });
  const paused = schedulerPaused(scheduler?.operations?.config?.scheduler ?? null);
  // A drain stops every host starting work, so it shows on every page too. The demo does not offer it.
  const drainAnswered = useAnswers("Drain");
  const [{ data: drain }, refetchDrain] = useTraxQuery(drainDocument, { ignoreRefreshStatus: true, pause: !drainAnswered });
  const drained = drainAnswered ? drainMark(drain?.operations?.config?.drain) : null;
  // A drain set or ended anywhere (a deploy, another console, the API) reaches the hub as a scheduler
  // change, and one that lapses sends nothing, so the mark reads again at its until.
  useRefetchOnChange("SCHEDULER_CONFIG", () => {
    if (drainAnswered) refetchDrain();
  });
  const lapsesAt = drained ? drain?.operations?.config?.drain?.until : null;
  useEffect(() => {
    if (!lapsesAt) return;
    const t = setTimeout(refetchDrain, drainRecheckDelay(lapsesAt, Date.now()));
    return () => clearTimeout(t);
  }, [lapsesAt, refetchDrain]);
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
          data-tone={environmentTone(environment)}
          className={`px-2 py-0.5 rounded-md border font-semibold ${ENVIRONMENT_BADGE[environmentTone(environment)]}`}
        >
          Environment: {environment}
        </span>
      )}
      {paused && (
        <Link
          to="/system/scheduler"
          className="flex items-center gap-1 rounded px-2 py-0.5 font-semibold bg-warn-soft text-warn-fg hover:underline"
        >
          <CirclePause className="size-3.5" />
          {paused}
        </Link>
      )}
      {drained && (
        <Link
          to="/system/scheduler"
          className="flex items-center gap-1 rounded px-2 py-0.5 font-semibold bg-warn-soft text-warn-fg hover:underline"
        >
          <CircleStop className="size-3.5" />
          {drained}
        </Link>
      )}
      <Search />
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
          <span title="Lists update live over the subscription; set an auto-refresh interval in Preferences">
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

