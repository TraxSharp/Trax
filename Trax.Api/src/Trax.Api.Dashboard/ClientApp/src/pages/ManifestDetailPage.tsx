import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import {
  EXECUTIONS,
  MANIFEST_DETAIL,
  MANIFEST_EXCLUSIONS,
  MANIFEST_STATS,
} from "../graphql/queries";
import {
  CANCEL_MANIFEST,
  DISABLE_MANIFEST,
  ENABLE_MANIFEST,
  SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY,
  TRIGGER_MANIFEST,
  TRIGGER_MANIFEST_DELAYED,
} from "../graphql/mutations";
import {
  BackLink,
  DetailPanel,
  Fields,
  JsonPanel,
  Loading,
  NotFound,
} from "../components/detail";
import { enumLabel, formatTime } from "../lib/format";
import { ManifestEditForm } from "../components/ManifestEditForm";
import { StateBadge } from "../components/StateBadge";
import { toast } from "../lib/toast";
import type {
  ExecutionSummary,
  ManifestDetail,
  ManifestExclusion,
  ManifestExecutionStats,
  PagedResult,
} from "../types";

interface ManData {
  operations: { manifestDetail: ManifestDetail | null };
}

export function ManifestDetailPage() {
  const id = Number(useParams().id);
  const [result, reexecute] = useQuery<ManData>({
    query: MANIFEST_DETAIL,
    variables: { id },
  });
  const [, trigger] = useMutation(TRIGGER_MANIFEST);
  const [, triggerDelayed] = useMutation(TRIGGER_MANIFEST_DELAYED);
  const [, cancelManifest] = useMutation(CANCEL_MANIFEST);
  const [, enable] = useMutation(ENABLE_MANIFEST);
  const [, disable] = useMutation(DISABLE_MANIFEST);
  const [, setReplay] = useMutation(SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const m = result.data?.operations?.manifestDetail;

  if (result.error && !m)
    return (
      <>
        <BackLink to="/manifests" label="Manifests" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !m) return <Loading />;
  if (!m) return <NotFound what="manifest" />;

  const refetch = () => reexecute({ requestPolicy: "network-only" });
  async function run(fn: typeof trigger, confirmMsg?: string) {
    if (confirmMsg && !confirm(confirmMsg)) return;
    const r = await fn({ externalId: m!.externalId });
    if (r.error) toast(r.error.message, "error");
    refetch();
  }

  // Run now; with askAfresh, a queued retry the trigger releases asks the model afresh instead of
  // replaying the failed run's decisions.
  async function onTrigger(askAfresh: boolean) {
    const what = askAfresh
      ? `Run "${shortName(m!.name)}" now, asking afresh?`
      : `Trigger "${shortName(m!.name)}" now?`;
    if (!confirm(what)) return;
    setBusy(true);
    const r = await trigger({ externalId: m!.externalId, askAfresh });
    setBusy(false);
    const res = r.data?.operations?.triggerManifest;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(res.message || `${shortName(m!.name)} has been queued.`, "success");
    else toast(res?.message ?? "Could not trigger.", "error");
    refetch();
  }

  // Through setManifestsReplayDecisionsOnRetry, the call the Blazor switch makes. On a refusal the
  // switch keeps showing the stored value, since it renders from the query.
  async function onReplayChange(replay: boolean) {
    setBusy(true);
    const r = await setReplay({ ids: [m!.id], replay });
    setBusy(false);
    const res = r.data?.operations?.setManifestsReplayDecisionsOnRetry;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success)
      toast(res.message || (replay ? "Retries replay decisions." : "Retries ask afresh."), "success");
    else toast(res?.message ?? "Could not change the retry setting.", "error");
    refetch();
  }

  async function onTriggerLater() {
    const mins = prompt("Trigger in how many minutes?", "5");
    if (mins == null) return;
    const n = Number(mins);
    if (!Number.isFinite(n) || n <= 0) {
      toast("Enter a positive number of minutes.", "error");
      return;
    }
    const delay = `PT${Math.floor(n)}M`; // ISO 8601 duration
    const r = await triggerDelayed({ externalId: m!.externalId, delay });
    if (r.error) toast(r.error.message, "error");
    else toast(`Scheduled in ${Math.floor(n)}m.`, "success");
  }

  return (
    <div>
      <BackLink to="/manifests" label="Manifests" />
      <div className="flex items-center gap-3 mt-2 mb-6">
        <h1 className="text-2xl font-bold text-fg">
          {shortName(m.name)}
        </h1>
        <span
          className={`text-xs px-2 py-0.5 rounded-full ${
            m.isEnabled
              ? "bg-ok-soft text-ok-fg"
              : "bg-raised text-muted"
          }`}
        >
          {m.isEnabled ? "Enabled" : "Disabled"}
        </span>
      </div>

      <StatCards manifestId={m.id} />

      <DetailPanel title="Details">
        <Fields
          rows={[
            ["ID", m.id],
            ["External ID", m.externalId],
            ["Name", m.name],
            ["Schedule type", m.scheduleType],
            ["Cron", m.cronExpression ?? "—"],
            ["Interval (s)", m.intervalSeconds ?? "—"],
            ["Variance (s)", m.varianceSeconds ?? "—"],
            ["Scheduled at", formatTime(m.scheduledAt)],
            ["Next scheduled run", formatTime(m.nextScheduledRun)],
            ["Misfire policy", m.misfirePolicy ? enumLabel(m.misfirePolicy) : "—"],
            ["Misfire threshold (s)", m.misfireThresholdSeconds ?? "—"],
            ["Max retries", m.maxRetries],
            ["Timeout (s)", m.timeoutSeconds ?? "—"],
            ["Priority", m.priority],
            [
              "Group",
              <Link className="text-accent-fg hover:underline" to={`/groups/${m.manifestGroupId}`}>
                {m.manifestGroupName ?? `#${m.manifestGroupId}`}
              </Link>,
            ],
            [
              "Depends on",
              m.dependsOnManifestId ? (
                <Link className="text-accent-fg hover:underline" to={`/manifests/${m.dependsOnManifestId}`}>
                  #{m.dependsOnManifestId}
                </Link>
              ) : (
                "—"
              ),
            ],
            ["Last success", formatTime(m.lastSuccessfulRun, "Never")],
            [
              "Queue and dead letters",
              <span>
                <Link className="text-accent-fg hover:underline" to={`/work-queue?manifestId=${m.id}`}>
                  Work queue entries
                </Link>
                {" · "}
                <Link className="text-accent-fg hover:underline" to={`/dead-letters?manifestId=${m.id}`}>
                  Dead letters
                </Link>
              </span>,
            ],
            [
              "Replay decisions on retry",
              <label
                className="inline-flex items-center gap-2 cursor-pointer"
                title="On: a retry of a failed run replays the decisions that run recorded. Off: retries ask the model afresh."
              >
                <input
                  type="checkbox"
                  role="switch"
                  aria-label="Replay decisions on retry"
                  checked={m.replayDecisionsOnRetry !== false}
                  disabled={busy}
                  onChange={(e) => onReplayChange(e.target.checked)}
                />
                {m.replayDecisionsOnRetry !== false ? "Yes" : "No (retries ask afresh)"}
              </label>,
            ],
          ]}
        />
        <div className="mt-4 flex flex-wrap gap-4">
          <button
            onClick={() => onTrigger(false)}
            disabled={busy}
            className="text-sm text-accent-fg hover:underline disabled:opacity-50"
          >
            Trigger
          </button>
          <button
            onClick={() => onTrigger(true)}
            disabled={busy}
            title="Run now; if this releases a queued retry, it asks the model afresh instead of replaying the failed run's decisions."
            className="text-sm text-accent-fg hover:underline disabled:opacity-50"
          >
            Trigger, ask afresh
          </button>
          <button
            onClick={onTriggerLater}
            className="text-sm text-accent-fg hover:underline"
          >
            Trigger later
          </button>
          <button
            onClick={() => setEditing(true)}
            className="text-sm text-accent-fg hover:underline"
          >
            Edit
          </button>
          <button
            onClick={() =>
              run(cancelManifest, `Cancel all running executions of "${shortName(m.name)}"?`)
            }
            className="text-sm text-danger-fg hover:underline"
          >
            Cancel runs
          </button>
          {m.isEnabled ? (
            <button onClick={() => run(disable)} className="text-sm text-fg-2 hover:underline">
              Disable
            </button>
          ) : (
            <button onClick={() => run(enable)} className="text-sm text-ok-fg hover:underline">
              Enable
            </button>
          )}
        </div>
      </DetailPanel>

      <JsonPanel
        title={m.propertyTypeName ? `Properties (${shortName(m.propertyTypeName)})` : "Properties"}
        json={m.properties}
      />

      <ExclusionsPanel manifestId={m.id} />
      <ExecutionHistory manifestId={m.id} />

      {editing && (
        <ManifestEditForm
          manifest={m}
          onSaved={refetch}
          onClose={() => setEditing(false)}
        />
      )}
    </div>
  );
}

// ── Summary cards ──────────────────────────────────────────────────────────
function StatCards({ manifestId }: { manifestId: number }) {
  const [{ data }] = useQuery<{ operations: { manifestStats: ManifestExecutionStats } }>({
    query: MANIFEST_STATS,
    variables: { manifestId },
  });
  const s = data?.operations?.manifestStats;
  if (!s) return null;
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-6">
      <StatCard label="Total runs" value={s.total} />
      <StatCard label="Completed" value={s.completed} tone="text-ok-fg" />
      <StatCard label="Failed" value={s.failed} tone="text-danger-fg" />
      <StatCard label="In progress" value={s.inProgress} tone="text-info-fg" />
    </div>
  );
}

function StatCard({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-4">
      <p className="text-xs text-muted">{label}</p>
      <p className={`text-2xl font-bold mt-1 ${tone ?? "text-fg"}`}>
        {value}
      </p>
    </div>
  );
}

// ── Exclusion windows ───────────────────────────────────────────────────────
function ExclusionsPanel({ manifestId }: { manifestId: number }) {
  const [{ data }] = useQuery<{ operations: { manifestExclusions: ManifestExclusion[] } }>({
    query: MANIFEST_EXCLUSIONS,
    variables: { manifestId },
  });
  const rows = data?.operations?.manifestExclusions ?? [];
  if (rows.length === 0) return null;
  return (
    <DetailPanel title="Exclusion windows">
      <ul className="space-y-2 text-sm text-fg-2">
        {rows.map((e, i) => (
          <li key={i} className="flex items-center gap-2">
            <span className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg">
              {exclusionKind(e.type)}
            </span>
            {describeExclusion(e)}
          </li>
        ))}
      </ul>
    </DetailPanel>
  );
}

function exclusionKind(type: ManifestExclusion["type"]): string {
  return type
    .toLowerCase()
    .replace(/_/g, " ")
    .replace(/^\w/, (c) => c.toUpperCase());
}

function describeExclusion(e: ManifestExclusion): string {
  switch (e.type) {
    case "DAYS_OF_WEEK":
      return `Skips ${(e.daysOfWeek ?? []).map(titleCase).join(", ")}`;
    case "DATES":
      return `Skips ${(e.dates ?? []).join(", ")}`;
    case "DATE_RANGE":
      return `Skips ${e.startDate} through ${e.endDate}`;
    case "TIME_WINDOW":
      return `Skips daily ${e.startTime} to ${e.endTime}`;
    default:
      return "";
  }
}

function titleCase(s: string): string {
  return s.charAt(0) + s.slice(1).toLowerCase();
}

// ── Recent execution history ────────────────────────────────────────────────
function ExecutionHistory({ manifestId }: { manifestId: number }) {
  const [{ data }] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: 10, manifestId },
  });
  const items = data?.operations?.executions?.items ?? [];
  return (
    <DetailPanel title="Recent executions">
      {items.length === 0 ? (
        <p className="text-sm text-muted">No executions yet.</p>
      ) : (
        <table className="w-full text-sm">
          <thead className="text-left text-muted">
            <tr>
              <th className="py-1 font-medium">ID</th>
              <th className="py-1 font-medium">State</th>
              <th className="py-1 font-medium">Started</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {items.map((e) => (
              <tr key={e.id}>
                <td className="py-1">
                  <Link
                    to={`/executions/${e.id}`}
                    className="text-accent-fg hover:underline"
                  >
                    #{e.id}
                  </Link>
                </td>
                <td className="py-1">
                  <StateBadge state={e.trainState} />
                </td>
                <td className="py-1 text-fg-2">
                  {new Date(e.startTime).toLocaleString()}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </DetailPanel>
  );
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
