import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { MANIFEST_SCHEDULES, MANIFESTS } from "../graphql/queries";
import {
  DISABLE_MANIFEST,
  ENABLE_MANIFEST,
  SET_MANIFESTS_ENABLED,
  SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY,
  TRIGGER_MANIFEST,
  TRIGGER_MANIFESTS,
} from "../graphql/mutations";
import { toast } from "../lib/toast";
import { reportBatchTrigger, type BatchTriggerReport } from "../lib/batchTrigger";
import { setHideAdminTrains, useHideAdminTrains } from "../lib/adminTrains";
import { BatchTriggerNotice } from "../components/BatchTriggerNotice";
import type { BatchTriggerResponse, OperationResponse } from "../types";
import { Pager } from "../components/Pager";
import { useKeyset } from "../lib/useKeyset";
import { useSelection } from "../lib/useSelection";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type { ManifestSchedule, ManifestSummary, PagedResult, ScheduleType } from "../types";
import { useAnswers } from "../lib/answerable";
import { useNow } from "../lib/useNow";
import { NextRun, SchedulesTabs } from "../components/schedules";
import { confirmAction } from "../lib/confirm";
import { useUrlState } from "../lib/urlState";
import { ApplyToAllMatching } from "../components/ApplyToAllMatching";
import { TermHelp } from "../components/TermHelp";
import { offersDecisions, useTrainsAskingDecisions } from "../lib/decisions";

const PAGE_SIZE = 25;
const SCHEDULES: (ScheduleType | "")[] = [
  "",
  "NONE",
  "CRON",
  "INTERVAL",
  "ON_DEMAND",
  "DEPENDENT",
  "DORMANT_DEPENDENT",
  "ONCE",
];

interface ManifestsData {
  operations: { manifests: PagedResult<ManifestSummary> };
}

interface SchedulesData {
  operations: { manifests: { items: ManifestSchedule[] } };
}

export function ManifestsPage() {
  const { afterId, isFirstPage, next, prev, reset } = useKeyset();
  const [enabled, setEnabled] = useUrlState("enabled");
  const [scheduleType, setScheduleType] = useUrlState<ScheduleType | "">("schedule");
  const [name, setName] = useUrlState("name");
  // The same "Hide admin trains" preference the Runs grid and the Overview use: it leaves out
  // the manifests of the scheduler's own trains, server side.
  const hideAdmin = useHideAdminTrains();
  const variables = {
    take: PAGE_SIZE,
    afterId,
    isEnabled: enabled === "" ? null : enabled === "true",
    scheduleType: scheduleType || null,
    nameContains: name || null,
    hideAdminTrains: hideAdmin,
  };
  const [result, reexecute] = useQuery<ManifestsData>({ query: MANIFESTS, variables });
  // The schedule in words and the next runs, read for the same page with the same filters and
  // zipped to its rows by id. Left out where the client cannot answer it (the demo, until the
  // recordings hold it); the row then shows the schedule from its type.
  const schedulesAnswered = useAnswers("ManifestSchedules");
  const [schedulesResult, reexecuteSchedules] = useQuery<SchedulesData>({
    query: MANIFEST_SCHEDULES,
    variables: { ...variables, count: 3 },
    pause: !schedulesAnswered,
  });
  const schedules = new Map(
    (schedulesResult.data?.operations?.manifests?.items ?? []).map((s) => [s.id, s]),
  );
  const now = useNow(30_000);
  const [, trigger] = useMutation(TRIGGER_MANIFEST);
  const [, enable] = useMutation(ENABLE_MANIFEST);
  const [, disable] = useMutation(DISABLE_MANIFEST);
  const [, setEnabledMany] = useMutation(SET_MANIFESTS_ENABLED);
  const [, setReplayMany] = useMutation(SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY);
  const [, triggerMany] = useMutation(TRIGGER_MANIFESTS);
  const [busy, setBusy] = useState(false);
  const [batchReport, setBatchReport] = useState<BatchTriggerReport | null>(null);
  const [batchError, setBatchError] = useState<string | null>(null);
  const { selected, toggle, setMany, clear } = useSelection();
  const refetchAll = () => {
    reexecute({ requestPolicy: "network-only" });
    if (schedulesAnswered) reexecuteSchedules({ requestPolicy: "network-only" });
  };
  useRefetchOnChange("MANIFEST", refetchAll);
  usePoll(refetchAll);

  const page = result.data?.operations?.manifests;
  const rows = page?.items ?? [];
  const selectedIds = [...selected];
  const askingDecisions = useTrainsAskingDecisions();
  const selectedAsk = offersDecisions(askingDecisions, rows.filter((r) => selected.has(r.id)).map((r) => r.name));
  const allSelected = rows.length > 0 && rows.every((m) => selected.has(m.id));
  const refetch = refetchAll;

  // One manifest's Queue, Disable or Enable from its row. The server answers success: false for a
  // manifest it no longer has; that is said, as an accepted change is.
  async function run(
    action: typeof trigger,
    field: "triggerManifest" | "enableManifest" | "disableManifest",
    externalId: string,
    confirm?: { title: string; message: string },
  ) {
    if (confirm && !(await confirmAction(confirm))) return;
    const r = await action({ externalId });
    const res: OperationResponse | undefined = r.data?.operations?.[field];
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(res.message || "Done.", "success");
    else toast(res?.message ?? "Nothing changed.", "error");
    refetch();
  }

  // Enable / disable and the replay-on-retry flag go to the API's batch mutations in one call, as
  // the Blazor page's batch buttons do. The response says how many manifests changed.
  async function bulkSet(
    label: string,
    call: () => Promise<{ error?: { message: string }; data?: { operations: Record<string, OperationResponse> } }>,
    field: string,
  ) {
    setBusy(true);
    const r = await call();
    setBusy(false);
    const res = r.data?.operations?.[field];
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(res.message || label, "success");
    else toast(res?.message ?? `${label} failed.`, "error");
    clear();
    refetch();
  }

  // Trigger Selected goes to triggerManifests in one call, the operations service call the Blazor
  // page's Trigger Selected makes. askAfresh: a queued retry the trigger releases asks the model
  // afresh instead of replaying the failed run's decisions. A refusal keeps the selection, so it
  // can be changed and sent again; an accepted batch clears it and shows each id it noted.
  async function triggerSelected(askAfresh: boolean) {
    const what = askAfresh
      ? `Queue ${selectedIds.length} manifest(s), asking afresh? A queued retry each releases asks its decider again instead of replaying the failed run's decisions.`
      : `Queue ${selectedIds.length} manifest(s) now?`;
    if (!(await confirmAction({ title: askAfresh ? "Queue, asking afresh" : "Queue manifests", message: what }))) return;
    setBusy(true);
    setBatchReport(null);
    setBatchError(null);
    const r = await triggerMany({ ids: selectedIds, askAfresh });
    setBusy(false);
    const res: BatchTriggerResponse | undefined = r.data?.operations?.triggerManifests;
    if (r.error || !res) {
      setBatchError(r.error?.message ?? "The queue request did not answer.");
      return;
    }
    const report = reportBatchTrigger(res);
    setBatchReport(report);
    if (report.refused) return;
    toast(report.message, report.severity);
    clear();
    refetch();
  }

  return (
    <div>
      <SchedulesTabs />
      <div className="flex items-center justify-between mb-6 gap-4 flex-wrap">
        <div className="flex items-center gap-2">
        <h1 className="text-2xl font-bold text-fg">Manifests</h1>
        <TermHelp term="manifest" />
      </div>
        <div className="flex gap-2">
          <input
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              clear();
              reset();
            }}
            placeholder="Filter by name…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-56"
          />
          <select
            value={scheduleType}
            onChange={(e) => {
              setScheduleType(e.target.value as ScheduleType | "");
              clear();
              reset();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            {SCHEDULES.map((s) => (
              <option key={s} value={s}>
                {s === "" ? "All schedules" : label(s)}
              </option>
            ))}
          </select>
          <select
            value={enabled}
            onChange={(e) => {
              setEnabled(e.target.value);
              clear();
              reset();
            }}
            className="text-sm border border-line-strong rounded-md px-2 py-1"
          >
            <option value="">All</option>
            <option value="true">Enabled</option>
            <option value="false">Disabled</option>
          </select>
          <label
            className="flex items-center gap-2 text-sm text-fg-2 whitespace-nowrap"
            title="Hide the manifests of the internal scheduler trains (JobDispatcher, ManifestManager, JobRunner, cleanup)"
          >
            <input
              type="checkbox"
              checked={hideAdmin}
              onChange={(e) => {
                setHideAdminTrains(e.target.checked);
                clear();
                reset();
              }}
            />
            Hide scheduler trains
          </label>
        </div>
      </div>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <BatchTriggerNotice report={batchReport} error={batchError} />

      {selectedIds.length > 0 && (
        <div className="flex flex-wrap items-center gap-3 mb-3 px-4 py-2 rounded-lg bg-accent-soft border border-accent-line text-sm">
          <span className="text-accent-fg font-medium">
            {selectedIds.length} selected
          </span>
          <button
            disabled={busy}
            onClick={() => triggerSelected(false)}
            className="text-accent-fg font-medium hover:underline disabled:opacity-50"
          >
            Queue selected
          </button>
          {selectedAsk && (
            <button
              disabled={busy}
              title="Queue; a queued retry this releases asks its decider afresh instead of replaying the failed run's decisions."
              onClick={() => triggerSelected(true)}
              className="text-accent-fg font-medium hover:underline disabled:opacity-50"
            >
              Queue selected, ask afresh
            </button>
          )}
          <button
            disabled={busy}
            onClick={() =>
              bulkSet("Manifests enabled.", () => setEnabledMany({ ids: selectedIds, enabled: true }), "setManifestsEnabled")
            }
            className="text-ok-fg font-medium hover:underline disabled:opacity-50"
          >
            Enable selected
          </button>
          <button
            disabled={busy}
            onClick={() =>
              bulkSet("Manifests disabled.", () => setEnabledMany({ ids: selectedIds, enabled: false }), "setManifestsEnabled")
            }
            className="text-fg-2 font-medium hover:underline disabled:opacity-50"
          >
            Disable selected
          </button>
          <ApplyToAllMatching
            total={page?.totalCount}
            // The list read's filter, as its variables send it.
            filter={{
              manifests: {
                isEnabled: enabled === "" ? null : enabled === "true",
                scheduleType: scheduleType || null,
                nameContains: name || null,
                hideAdminTrains: hideAdmin,
              },
            }}
            actions={[
              { kind: "TRIGGER", explain: "A run of each is queued now.", danger: false },
              { kind: "ENABLE", explain: "Each runs on its schedule again.", danger: false },
              { kind: "DISABLE", explain: "None runs on its schedule until it is enabled." },
            ]}
          />
          {selectedAsk && (
            <button
              disabled={busy}
              title="Retries of the selected manifests replay the decisions the failed run recorded."
              onClick={() =>
                bulkSet(
                  "Retries replay decisions.",
                  () => setReplayMany({ ids: selectedIds, replay: true }),
                  "setManifestsReplayDecisionsOnRetry",
                )
              }
              className="text-accent-fg font-medium hover:underline disabled:opacity-50"
            >
              Replay on retry
            </button>
          )}
          {selectedAsk && (
            <button
              disabled={busy}
              title="Retries of the selected manifests ask the model afresh instead of replaying the failed run's decisions."
              onClick={() =>
                bulkSet(
                  "Retries ask afresh.",
                  () => setReplayMany({ ids: selectedIds, replay: false }),
                  "setManifestsReplayDecisionsOnRetry",
                )
              }
              className="text-accent-fg font-medium hover:underline disabled:opacity-50"
            >
              Ask afresh on retry
            </button>
          )}
          <button
            onClick={clear}
            className="ml-auto text-muted hover:underline"
          >
            Clear
          </button>
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 w-8">
                <input
                  type="checkbox"
                  aria-label="Select all"
                  checked={allSelected}
                  disabled={rows.length === 0}
                  onChange={(e) => setMany(rows.map((m) => m.id), e.target.checked)}
                />
              </th>
              <th className="px-4 py-2 font-medium">Manifest</th>
              <th className="px-4 py-2 font-medium">Schedule</th>
              <th className="px-4 py-2 font-medium">Next run</th>
              <th className="px-4 py-2 font-medium">Enabled</th>
              <th className="px-4 py-2 font-medium">Retries</th>
              <th className="px-4 py-2 font-medium">Last success</th>
              <th className="px-4 py-2 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {page?.items.map((m) => (
              <tr key={m.id}>
                <td className="px-4 py-2">
                  <input
                    type="checkbox"
                    aria-label={`Select #${m.id}`}
                    checked={selected.has(m.id)}
                    onChange={() => toggle(m.id)}
                  />
                </td>
                <td className="px-4 py-2">
                  <Link
                    to={`/schedules/${m.id}`}
                    className="font-medium text-accent-fg hover:underline"
                    title={m.name}
                  >
                    {shortName(m.name)}
                  </Link>
                  <span className="block text-xs text-muted">{m.externalId}</span>
                </td>
                <td className="px-4 py-2 text-fg-2" title={m.cronExpression ?? undefined}>
                  {schedules.get(m.id)?.scheduleText ?? schedule(m)}
                </td>
                <td className="px-4 py-2 whitespace-nowrap">
                  <NextRun runs={schedules.get(m.id)?.nextRuns} now={now} />
                </td>
                <td className="px-4 py-2">
                  <span
                    className={`text-xs px-2 py-0.5 rounded-full ${
                      m.isEnabled
                        ? "bg-ok-soft text-ok-fg"
                        : "bg-raised text-muted"
                    }`}
                  >
                    {m.isEnabled ? "Enabled" : "Disabled"}
                  </span>
                </td>
                <td
                  className="px-4 py-2 text-fg-2 text-xs"
                  title="What a retry of a failed run does with the decisions that run recorded"
                >
                  {m.replayDecisionsOnRetry === false ? "Ask afresh" : "Replay"}
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {m.lastSuccessfulRun
                    ? new Date(m.lastSuccessfulRun).toLocaleString()
                    : "—"}
                </td>
                <td className="px-4 py-2 text-right whitespace-nowrap">
                  <button
                    onClick={() =>
                      run(trigger, "triggerManifest", m.externalId, {
                        title: "Queue manifest",
                        message: `Queue "${shortName(m.name)}" now?`,
                      })
                    }
                    className="text-accent-fg hover:underline mr-3"
                  >
                    Queue
                  </button>
                  {m.isEnabled ? (
                    <button
                      onClick={() => run(disable, "disableManifest", m.externalId)}
                      className="text-fg-2 hover:underline"
                    >
                      Disable
                    </button>
                  ) : (
                    <button
                      onClick={() => run(enable, "enableManifest", m.externalId)}
                      className="text-ok-fg hover:underline"
                    >
                      Enable
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {page?.items.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-muted">
                  No manifests.{" "}
                  <a
                    className="text-accent-fg hover:underline"
                    href="https://traxsharp.net/docs/scheduler/setup"
                    target="_blank"
                    rel="noreferrer"
                  >
                    Schedule a train
                  </a>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <Pager
        total={page?.totalCount}
        isEstimated={page?.isEstimatedCount}
        isFirstPage={isFirstPage}
        nextCursor={page?.nextCursor}
        onPrev={prev}
        onNext={next}
      />
    </div>
  );
}

function schedule(m: ManifestSummary): string {
  switch (m.scheduleType) {
    case "CRON":
      return m.cronExpression ?? "Cron";
    case "INTERVAL":
      return m.intervalSeconds ? `Every ${m.intervalSeconds}s` : "Interval";
    default:
      return label(m.scheduleType);
  }
}

function label(s: string): string {
  return s
    .toLowerCase()
    .replace(/_/g, " ")
    .replace(/^\w/, (c) => c.toUpperCase());
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
