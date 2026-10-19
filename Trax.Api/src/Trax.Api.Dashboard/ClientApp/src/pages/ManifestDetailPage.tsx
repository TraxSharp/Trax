import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import {
  EXECUTIONS,
  MANIFEST_DETAIL,
  MANIFEST_EXCLUSIONS,
  MANIFEST_SCHEDULE,
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
  ManifestSchedule,
  PagedResult,
} from "../types";
import { useAnswers } from "../lib/answerable";
import { useNow } from "../lib/useNow";
import { RunSparkline } from "../components/schedules";
import { untilText, utcText } from "../lib/schedules";
import { confirmAction } from "../lib/confirm";
import { Modal } from "../components/Modal";
import { offersDecisions, useTrainsAskingDecisions } from "../lib/decisions";

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
  const [later, setLater] = useState(false);
  const m = result.data?.operations?.manifestDetail;
  const askingDecisions = useTrainsAskingDecisions();

  if (result.error && !m)
    return (
      <>
        <BackLink to="/schedules" label="Manifests" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !m) return <Loading />;
  if (!m) return <NotFound what="manifest" />;

  const refetch = () => reexecute({ requestPolicy: "network-only" });
  async function onCancelRuns() {
    if (
      !(await confirmAction({
        title: "Cancel runs",
        message: `Cancel every running run of "${shortName(m!.name)}"? A running run stops at its next junction.`,
        danger: true,
      }))
    )
      return;
    const r = await cancelManifest({ externalId: m!.externalId });
    const res = r.data?.operations?.cancelManifest;
    if (r.error) toast(r.error.message, "error");
    else if (!res?.success) toast(res?.message ?? "Could not cancel its runs.", "error");
    else if (res.count === 0) toast("No run of it is pending or running.", "info");
    else toast(`Cancellation requested for ${res.count} run(s).`, "success");
    refetch();
  }

  // Run now; with askAfresh, a queued retry the trigger releases asks the model afresh instead of
  // replaying the failed run's decisions.
  async function onTrigger(askAfresh: boolean) {
    const what = askAfresh
      ? `Queue "${shortName(m!.name)}" now, asking afresh?`
      : `Queue "${shortName(m!.name)}" now?`;
    if (!(await confirmAction({ title: askAfresh ? "Queue now, asking afresh" : "Queue now", message: what }))) return;
    setBusy(true);
    const r = await trigger({ externalId: m!.externalId, askAfresh });
    setBusy(false);
    const res = r.data?.operations?.triggerManifest;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(res.message || `${shortName(m!.name)} has been queued.`, "success");
    else toast(res?.message ?? "Could not queue it.", "error");
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

  // The switch beside the heading. Disabling asks first, since the manifest then stops running on
  // its schedule; enabling does not.
  async function onEnabledChange(on: boolean) {
    if (
      !on &&
      !(await confirmAction({
        title: "Disable manifest",
        message: `Disable "${shortName(m!.name)}"? It does not run on its schedule until it is enabled again.`,
        danger: true,
      }))
    )
      return;
    setBusy(true);
    const r = await (on ? enable : disable)({ externalId: m!.externalId });
    setBusy(false);
    if (r.error) toast(r.error.message, "error");
    refetch();
  }

  async function onQueueLater(minutes: number) {
    setLater(false);
    const delay = `PT${minutes}M`; // ISO 8601 duration
    const r = await triggerDelayed({ externalId: m!.externalId, delay });
    const res = r.data?.operations?.triggerManifestDelayed;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(`Queued to run in ${minutes} min.`, "success");
    else toast(res?.message ?? "Could not queue it.", "error");
    refetch();
  }

  return (
    <div>
      <BackLink to="/schedules" label="Manifests" />
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
        <label className="ml-auto inline-flex items-center gap-2 text-sm text-fg-2 cursor-pointer">
          <input
            type="checkbox"
            role="switch"
            aria-label="Manifest enabled"
            checked={m.isEnabled}
            disabled={busy}
            onChange={(e) => onEnabledChange(e.target.checked)}
          />
          Runs on its schedule
        </label>
      </div>

      <StatCards manifestId={m.id} />
      {/* Keyed by the switch, so enabling or disabling reads the next runs again. */}
      <SchedulePanel key={String(m.isEnabled)} manifestId={m.id} isEnabled={m.isEnabled} />
      <RunSparkline scope="MANIFEST" id={m.id} />

      <DetailPanel title="Details">
        <Fields
          rows={[
            ["ID", m.id],
            ["External ID", m.externalId],
            ["Name", m.name],
            ["Schedule type", enumLabel(m.scheduleType)],
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
              <Link className="text-accent-fg hover:underline" to={`/schedules/groups/${m.manifestGroupId}`}>
                {m.manifestGroupName ?? `#${m.manifestGroupId}`}
              </Link>,
            ],
            [
              "Depends on",
              m.dependsOnManifestId ? (
                <Link className="text-accent-fg hover:underline" to={`/schedules/${m.dependsOnManifestId}`}>
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
                <Link className="text-accent-fg hover:underline" to={`/runs/queued?manifestId=${m.id}`}>
                  Work queue entries
                </Link>
                {" · "}
                <Link className="text-accent-fg hover:underline" to={`/runs/dead-letters?manifestId=${m.id}`}>
                  Dead letters
                </Link>
              </span>,
            ],
            ...(offersDecisions(askingDecisions, [m.name])
              ? ([
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
                ] as [string, React.ReactNode][])
              : []),
          ]}
        />
        <div className="mt-4 flex flex-wrap gap-4">
          <button
            onClick={() => onTrigger(false)}
            disabled={busy}
            className="text-sm text-accent-fg hover:underline disabled:opacity-50"
          >
            Queue now
          </button>
          {offersDecisions(askingDecisions, [m.name]) && (
            <button
              onClick={() => onTrigger(true)}
              disabled={busy}
              title="Run now; if this releases a queued retry, it asks the model afresh instead of replaying the failed run's decisions."
              className="text-sm text-accent-fg hover:underline disabled:opacity-50"
            >
              Queue now, ask afresh
            </button>
          )}
          <button
            onClick={() => setLater(true)}
            className="text-sm text-accent-fg hover:underline"
          >
            Queue later
          </button>
          {later && <QueueLaterDialog onQueue={onQueueLater} onClose={() => setLater(false)} />}
          <button
            onClick={() => setEditing(true)}
            className="text-sm text-accent-fg hover:underline"
          >
            Edit
          </button>
          <button
            onClick={onCancelRuns}
            className="text-sm text-danger-fg hover:underline"
          >
            Cancel runs
          </button>
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

// ── When it runs ───────────────────────────────────────────────────────────
// The schedule in words and the next runs, as the manifest manager's own due-ness check reads
// them. Not drawn where the client cannot answer it (the demo, until the recordings hold it).
function SchedulePanel({ manifestId, isEnabled }: { manifestId: number; isEnabled: boolean }) {
  const answered = useAnswers("ManifestSchedule");
  const [{ data }] = useQuery<{ operations: { manifestDetail: ManifestSchedule | null } }>({
    query: MANIFEST_SCHEDULE,
    variables: { id: manifestId, count: 5 },
    pause: !answered,
    requestPolicy: "cache-and-network",
  });
  const now = useNow(30_000);
  const s = data?.operations?.manifestDetail;
  if (!answered || !s) return null;
  return (
    <section aria-label="Schedule" className="mb-6 rounded-lg border border-line bg-surface p-4">
      <h2 className="text-sm font-semibold text-fg">Schedule</h2>
      <p className="mt-1 text-lg text-fg">{s.scheduleText}</p>
      {s.nextRuns.length === 0 ? (
        <p className="mt-2 text-sm text-muted">
          {isEnabled
            ? "No upcoming run: it runs when triggered or when what it depends on succeeds."
            : "No upcoming run while it is disabled."}
        </p>
      ) : (
        <>
          <h3 className="mt-3 text-xs font-medium text-muted">Next runs</h3>
          <ol className="mt-1 space-y-0.5 text-sm">
            {s.nextRuns.map((t) => (
              <li key={t} className="flex gap-3">
                <span className="w-48 text-fg-2">{utcText(t)}</span>
                <span className="text-muted">{untilText(t, now)}</span>
              </li>
            ))}
          </ol>
        </>
      )}
    </section>
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

// ── Recent run history ────────────────────────────────────────────────
function ExecutionHistory({ manifestId }: { manifestId: number }) {
  const [{ data }] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: 10, manifestId },
  });
  const items = data?.operations?.executions?.items ?? [];
  return (
    <DetailPanel title="Recent runs">
      {items.length === 0 ? (
        <p className="text-sm text-muted">No runs yet.</p>
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
                    to={`/runs/${e.id}`}
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

/** Asks how many minutes from now the manifest should be queued. */
function QueueLaterDialog({ onQueue, onClose }: { onQueue: (minutes: number) => void; onClose: () => void }) {
  const [text, setText] = useState("5");
  const minutes = Math.floor(Number(text));
  const valid = Number.isFinite(minutes) && minutes > 0;
  return (
    <Modal title="Queue later" onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (valid) onQueue(minutes);
        }}
      >
        <label className="block text-sm text-fg-2">
          Minutes from now
          <input
            autoFocus
            type="number"
            min={1}
            value={text}
            onChange={(e) => setText(e.target.value)}
            className="mt-1 w-full rounded-md border border-line bg-field px-3 py-1.5 text-sm text-fg"
          />
        </label>
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-md px-3 py-1.5 text-sm text-fg-2 hover:bg-hover">
            Keep it
          </button>
          <button
            type="submit"
            disabled={!valid}
            className="rounded-md bg-accent px-3 py-1.5 text-sm font-medium text-on-accent hover:bg-accent-hover disabled:opacity-50"
          >
            Queue later
          </button>
        </div>
      </form>
    </Modal>
  );
}
