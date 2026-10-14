import { useMemo, useState } from "react";
import { useMutation, useQuery } from "urql";
import { Link, useNavigate } from "react-router-dom";
import {
  GROUP_DEPENDENCY_GRAPH,
  MANIFEST_GROUPS,
  MANIFEST_GROUP_STATS,
} from "../graphql/queries";
import {
  CANCEL_GROUPS,
  SET_ALL_MANIFEST_GROUPS_ENABLED,
  SET_MANIFEST_GROUPS_ENABLED,
  TRIGGER_GROUPS,
} from "../graphql/mutations";
import { reportBatchTrigger, type BatchTriggerReport } from "../lib/batchTrigger";
import { BatchTriggerNotice } from "../components/BatchTriggerNotice";
import { useSelection } from "../lib/useSelection";
import { toast } from "../lib/toast";
import { Pager } from "../components/Pager";
import { DagGraph } from "../components/DagGraph";
import { computeDagLayout } from "../lib/dagLayout";
import { useKeyset } from "../lib/useKeyset";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type {
  BatchTriggerResponse,
  OperationResponse,
  ManifestGroupDependencyGraph,
  ManifestGroupStats,
  ManifestGroupSummary,
  PagedResult,
} from "../types";

const PAGE_SIZE = 25;

interface GroupsData {
  operations: { manifestGroups: { groups: PagedResult<ManifestGroupSummary> } };
}

export function ManifestGroupsPage() {
  const { afterId, isFirstPage, next, prev, reset } = useKeyset();
  const [name, setName] = useState("");
  const navigate = useNavigate();
  const [result, reexecute] = useQuery<GroupsData>({
    query: MANIFEST_GROUPS,
    variables: { take: PAGE_SIZE, afterId, nameContains: name || null },
  });
  useRefetchOnChange("MANIFEST_GROUP", () => reexecute({ requestPolicy: "network-only" }));
  usePoll(() => reexecute({ requestPolicy: "network-only" }));
  const { selected, toggle, setMany, clear } = useSelection();
  const [busy, setBusy] = useState(false);
  const [, setGroupsEnabled] = useMutation(SET_MANIFEST_GROUPS_ENABLED);
  const [, setAllGroupsEnabled] = useMutation(SET_ALL_MANIFEST_GROUPS_ENABLED);
  const [, triggerGroups] = useMutation(TRIGGER_GROUPS);
  const [, cancelGroups] = useMutation(CANCEL_GROUPS);
  const [batchReport, setBatchReport] = useState<BatchTriggerReport | null>(null);
  const [batchError, setBatchError] = useState<string | null>(null);

  const page = result.data?.operations?.manifestGroups?.groups;
  const rows = page?.items ?? [];
  const selectedIds = [...selected];
  const allSelected = rows.length > 0 && rows.every((g) => selected.has(g.id));

  // Both go through the operations service the Blazor page calls. "All" acts on every group, not
  // just this page, and leaves the selection alone; "selected" clears it.
  async function onSetAll(enabled: boolean) {
    if (!confirm(`${enabled ? "Enable" : "Disable"} every manifest group?`)) return;
    setBusy(true);
    const r = await setAllGroupsEnabled({ enabled });
    setBusy(false);
    report(r.error?.message, r.data?.operations?.manifestGroups?.setAllManifestGroupsEnabled, enabled ? "All groups enabled." : "All groups disabled.");
    reexecute({ requestPolicy: "network-only" });
  }

  async function onSetSelected(enabled: boolean) {
    setBusy(true);
    const r = await setGroupsEnabled({ ids: selectedIds, enabled });
    setBusy(false);
    report(r.error?.message, r.data?.operations?.manifestGroups?.setManifestGroupsEnabled, enabled ? "Groups enabled." : "Groups disabled.");
    clear();
    reexecute({ requestPolicy: "network-only" });
  }

  // Trigger Selected and Cancel Running go through the operations service in one call each, as
  // the Blazor page's buttons do. A trigger counts manifests: a member that already had a queued
  // entry is counted apart from the ones queued, and a group that no longer exists is noted.
  async function onTriggerSelected() {
    if (!confirm(`Trigger every manifest in ${selectedIds.length} group(s)?`)) return;
    setBusy(true);
    setBatchReport(null);
    setBatchError(null);
    const r = await triggerGroups({ ids: selectedIds });
    setBusy(false);
    const res: BatchTriggerResponse | undefined = r.data?.operations?.triggerGroups;
    if (r.error || !res) {
      setBatchError(r.error?.message ?? "The trigger did not answer.");
      return;
    }
    const report = reportBatchTrigger(res);
    setBatchReport(report);
    if (report.refused) return;
    toast(report.message, report.severity);
    clear();
    reexecute({ requestPolicy: "network-only" });
  }

  async function onCancelSelected() {
    if (
      !confirm(
        `Request cancellation of every pending and running execution in ${selectedIds.length} group(s)?`,
      )
    )
      return;
    setBusy(true);
    setBatchReport(null);
    setBatchError(null);
    const r = await cancelGroups({ ids: selectedIds });
    setBusy(false);
    const res: OperationResponse | undefined = r.data?.operations?.cancelGroups;
    if (r.error || !res) {
      setBatchError(r.error?.message ?? "The cancellation did not answer.");
      return;
    }
    if (!res.success) {
      setBatchError(res.message ?? "The cancellation was refused.");
      return;
    }
    toast(res.message ?? "Cancellation requested.", res.count === 0 ? "warning" : "success");
    clear();
    reexecute({ requestPolicy: "network-only" });
  }

  // Global cross-group dependency graph. Refetches on the same signal as the grid, so adding or
  // wiring up a group updates the picture live.
  const [graphResult, reexecuteGraph] = useQuery<{
    operations: { manifestGroups: { dependencyGraph: ManifestGroupDependencyGraph } };
  }>({ query: GROUP_DEPENDENCY_GRAPH });
  useRefetchOnChange("MANIFEST_GROUP", () =>
    reexecuteGraph({ requestPolicy: "network-only" }),
  );
  const graph = graphResult.data?.operations?.manifestGroups?.dependencyGraph;
  const layout = useMemo(
    () =>
      computeDagLayout(
        (graph?.nodes ?? []).map((n) => ({
          id: n.id,
          label: n.name,
          isHighlighted: n.isHighlighted,
        })),
        (graph?.edges ?? []).map((e) => ({ fromId: e.fromId, toId: e.toId })),
      ),
    [graph],
  );

  // Fetch per-group stats for just the visible page, in one batched call (see the API notes on
  // why this is not folded into the list query: it is a heavier aggregation).
  const ids = (page?.items ?? []).map((g) => g.id);
  const [statsResult] = useQuery<{
    operations: { manifestGroups: { stats: ManifestGroupStats[] } };
  }>({
    query: MANIFEST_GROUP_STATS,
    variables: { groupIds: ids },
    pause: ids.length === 0,
  });
  const statsById = new Map(
    (statsResult.data?.operations?.manifestGroups?.stats ?? []).map((s) => [s.groupId, s]),
  );

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">
          Manifest groups
        </h1>
        <div className="flex items-center gap-2 ml-auto">
          <button
            onClick={() => onSetAll(true)}
            disabled={busy || rows.length === 0 || rows.every((g) => g.isEnabled)}
            className="text-sm px-3 py-1 rounded-md border border-ok-line text-ok-fg hover:bg-ok-soft disabled:opacity-50"
          >
            Enable all
          </button>
          <button
            onClick={() => onSetAll(false)}
            disabled={busy || rows.length === 0 || rows.every((g) => !g.isEnabled)}
            className="text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
          >
            Disable all
          </button>
        </div>
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
            onClick={onTriggerSelected}
            disabled={busy}
            className="text-accent-fg font-medium hover:underline disabled:opacity-50"
          >
            Trigger selected
          </button>
          <button
            onClick={() => onSetSelected(true)}
            disabled={busy}
            className="text-ok-fg font-medium hover:underline disabled:opacity-50"
          >
            Enable selected
          </button>
          <button
            onClick={() => onSetSelected(false)}
            disabled={busy}
            className="text-fg-2 font-medium hover:underline disabled:opacity-50"
          >
            Disable selected
          </button>
          <button
            onClick={onCancelSelected}
            disabled={busy}
            className="text-danger-fg font-medium hover:underline disabled:opacity-50"
          >
            Cancel running
          </button>
          <button onClick={clear} className="ml-auto text-muted hover:underline">
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
                  onChange={(e) => setMany(rows.map((g) => g.id), e.target.checked)}
                />
              </th>
              <th className="px-4 py-2 font-medium">Group</th>
              <th className="px-4 py-2 font-medium">Manifests</th>
              <th className="px-4 py-2 font-medium">Runs</th>
              <th className="px-4 py-2 font-medium">Failed</th>
              <th className="px-4 py-2 font-medium">Max active jobs</th>
              <th className="px-4 py-2 font-medium">Priority</th>
              <th className="px-4 py-2 font-medium">Enabled</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {page?.items.map((g) => (
              <tr key={g.id} className="hover:bg-hover">
                <td className="px-4 py-2">
                  <input
                    type="checkbox"
                    aria-label={`Select #${g.id}`}
                    checked={selected.has(g.id)}
                    onChange={() => toggle(g.id)}
                  />
                </td>
                <td className="px-4 py-2">
                  <Link
                    to={`/groups/${g.id}`}
                    className="font-medium text-accent-fg hover:underline"
                  >
                    {g.name}
                  </Link>
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {statsById.get(g.id)?.manifestCount ?? "—"}
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {statsById.get(g.id)?.totalExecutions ?? "—"}
                </td>
                <td className="px-4 py-2 text-danger-fg">
                  {statsById.get(g.id)?.failed ?? "—"}
                </td>
                <td className="px-4 py-2 text-fg-2">
                  {g.maxActiveJobs ?? "∞"}
                </td>
                <td className="px-4 py-2 text-fg-2">{g.priority}</td>
                <td className="px-4 py-2">
                  <span
                    className={`text-xs px-2 py-0.5 rounded-full ${
                      g.isEnabled
                        ? "bg-ok-soft text-ok-fg"
                        : "bg-raised text-muted"
                    }`}
                  >
                    {g.isEnabled ? "Enabled" : "Disabled"}
                  </span>
                </td>
              </tr>
            ))}
            {page?.items.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-muted">
                  No groups.
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

      {layout.nodes.length > 0 && (
        <div className="mt-8 bg-surface rounded-lg border border-line p-5">
          <h2 className="text-sm font-semibold text-fg mb-1">
            Dependency graph
          </h2>
          <p className="text-xs text-muted mb-3">
            Cross-group dependencies (parent → dependent). Click a group to open it.
          </p>
          <DagGraph layout={layout} onNodeClick={(id) => navigate(`/groups/${id}`)} />
        </div>
      )}
    </div>
  );
}

function report(error: string | undefined, res: OperationResponse | undefined, fallback: string) {
  if (error) toast(error, "error");
  else if (res?.success) toast(res.message || fallback, "success");
  else toast(res?.message ?? "Nothing changed.", "error");
}
