import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import {
  EXECUTIONS,
  MANIFEST_GROUP_DETAIL,
  MANIFEST_GROUP_STATS,
  MANIFESTS,
} from "../graphql/queries";
import {
  CANCEL_GROUP,
  TRIGGER_GROUP,
  UPDATE_MANIFEST_GROUP,
} from "../graphql/mutations";
import { StateBadge } from "../components/StateBadge";
import { DagGraph } from "../components/DagGraph";
import { computeDagLayout } from "../lib/dagLayout";
import type {
  ExecutionSummary,
  ManifestGroupDependencyGraph,
  ManifestGroupStats,
  ManifestGroupSummary,
  ManifestSummary,
  PagedResult,
} from "../types";

interface DetailData {
  operations: {
    manifestGroups: {
      group: ManifestGroupSummary | null;
      graph: ManifestGroupDependencyGraph | null;
    };
  };
}

export function ManifestGroupDetailPage() {
  const id = Number(useParams().id);
  const [result, reexecute] = useQuery<DetailData>({
    query: MANIFEST_GROUP_DETAIL,
    variables: { id },
  });
  const [, update] = useMutation(UPDATE_MANIFEST_GROUP);
  const [, triggerGroup] = useMutation(TRIGGER_GROUP);
  const [, cancelGroup] = useMutation(CANCEL_GROUP);

  const group = result.data?.operations?.manifestGroups?.group;
  const graph = result.data?.operations?.manifestGroups?.graph;
  const refetch = () => reexecute({ requestPolicy: "network-only" });

  if (result.error) return <ErrorBox message={result.error.message} />;
  if (!group) return <p className="text-muted">Loading…</p>;

  async function save(input: Record<string, unknown>) {
    const r = await update({ id, input });
    if (r.error) alert(r.error.message);
    refetch();
  }
  async function act(fn: typeof triggerGroup, msg: string) {
    if (!confirm(msg)) return;
    const r = await fn({ groupId: id });
    if (r.error) alert(r.error.message);
    refetch();
  }

  return (
    <div>
      <Link to="/groups" className="text-sm text-accent-fg hover:underline">
        ← All groups
      </Link>
      <h1 className="text-2xl font-bold text-fg mt-2 mb-6">{group.name}</h1>

      <GroupStatCards groupId={id} />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <GroupSettings
          key={group.id}
          group={group}
          onSave={save}
          onTrigger={() => act(triggerGroup, `Trigger every manifest in "${group.name}"?`)}
          onCancel={() => act(cancelGroup, `Cancel running executions in "${group.name}"?`)}
        />

        <div className="bg-surface rounded-lg border border-line p-5">
          <h2 className="text-sm font-semibold text-fg mb-4">
            Cross-group dependencies
          </h2>
          {graph ? (
            <Dag graph={graph} focalId={id} />
          ) : (
            <p className="text-muted text-sm">No graph.</p>
          )}
        </div>
      </div>

      <GroupManifests groupId={id} />
      <GroupExecutions groupId={id} />
    </div>
  );
}

// ── Group summary cards ─────────────────────────────────────────────────────
function GroupStatCards({ groupId }: { groupId: number }) {
  const [{ data }] = useQuery<{
    operations: { manifestGroups: { stats: ManifestGroupStats[] } };
  }>({ query: MANIFEST_GROUP_STATS, variables: { groupIds: [groupId] } });
  const s = data?.operations?.manifestGroups?.stats?.[0];
  if (!s) return null;
  return (
    <div className="grid grid-cols-2 md:grid-cols-5 gap-4 mb-6">
      <GroupStatCard label="Manifests" value={s.manifestCount} />
      <GroupStatCard label="Total runs" value={s.totalExecutions} />
      <GroupStatCard label="Completed" value={s.completed} tone="text-ok-fg" />
      <GroupStatCard label="Failed" value={s.failed} tone="text-danger-fg" />
      <GroupStatCard label="In progress" value={s.inProgress} tone="text-info-fg" />
    </div>
  );
}

function GroupStatCard({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-4">
      <p className="text-xs text-muted">{label}</p>
      <p className={`text-2xl font-bold mt-1 ${tone ?? "text-fg"}`}>
        {value}
      </p>
    </div>
  );
}

// ── Manifests in the group ──────────────────────────────────────────────────
function GroupManifests({ groupId }: { groupId: number }) {
  const [{ data }] = useQuery<{ operations: { manifests: PagedResult<ManifestSummary> } }>({
    query: MANIFESTS,
    variables: { take: 25, manifestGroupId: groupId },
  });
  const items = data?.operations?.manifests?.items ?? [];
  return (
    <div className="mt-6 bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-3">Manifests</h2>
      {items.length === 0 ? (
        <p className="text-sm text-muted">No manifests in this group.</p>
      ) : (
        <ul className="divide-y divide-line text-sm">
          {items.map((m) => (
            <li key={m.id} className="py-2 flex items-center justify-between">
              <Link
                to={`/manifests/${m.id}`}
                className="text-accent-fg hover:underline"
              >
                {shortName(m.name)}
              </Link>
              <span
                className={`text-xs px-2 py-0.5 rounded-full ${
                  m.isEnabled
                    ? "bg-ok-soft text-ok-fg"
                    : "bg-raised text-muted"
                }`}
              >
                {m.isEnabled ? "Enabled" : "Disabled"}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ── Recent executions across the group ──────────────────────────────────────
function GroupExecutions({ groupId }: { groupId: number }) {
  const [{ data }] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: 10, manifestGroupId: groupId },
  });
  const items = data?.operations?.executions?.items ?? [];
  return (
    <div className="mt-6 bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-3">
        Recent executions
      </h2>
      {items.length === 0 ? (
        <p className="text-sm text-muted">No executions yet.</p>
      ) : (
        <ul className="divide-y divide-line text-sm">
          {items.map((e) => (
            <li key={e.id} className="py-2 flex items-center justify-between">
              <Link
                to={`/executions/${e.id}`}
                className="text-accent-fg hover:underline"
              >
                {shortName(e.name)}
              </Link>
              <StateBadge state={e.trainState} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}

function GroupSettings({
  group,
  onSave,
  onTrigger,
  onCancel,
}: {
  group: ManifestGroupSummary;
  onSave: (input: Record<string, unknown>) => void;
  onTrigger: () => void;
  onCancel: () => void;
}) {
  const [maxActive, setMaxActive] = useState(
    group.maxActiveJobs?.toString() ?? "",
  );
  const [priority, setPriority] = useState(group.priority.toString());
  const [enabled, setEnabled] = useState(group.isEnabled);

  function save() {
    const input: Record<string, unknown> = maxActive
      ? { maxActiveJobs: Number(maxActive) }
      : { clearMaxActiveJobs: true };
    input.priority = Number(priority);
    input.isEnabled = enabled;
    onSave(input);
  }

  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-4">Settings</h2>
      <div className="space-y-4 text-sm">
        <Field label="Max active jobs (blank = unlimited)">
          <input
            value={maxActive}
            onChange={(e) => setMaxActive(e.target.value.replace(/\D/g, ""))}
            placeholder="∞"
            className="w-full border border-line-strong rounded-md px-2 py-1"
          />
        </Field>
        <Field label="Priority">
          <input
            value={priority}
            onChange={(e) => setPriority(e.target.value.replace(/[^\d-]/g, ""))}
            className="w-full border border-line-strong rounded-md px-2 py-1"
          />
        </Field>
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
          />
          Enabled
        </label>
        <button
          onClick={save}
          className="px-4 py-2 bg-accent text-on-accent rounded-lg hover:bg-accent-hover"
        >
          Save
        </button>
      </div>

      <div className="mt-6 pt-4 border-t border-line flex gap-3">
        <button onClick={onTrigger} className="text-sm text-accent-fg hover:underline">
          Trigger group
        </button>
        <button onClick={onCancel} className="text-sm text-danger-fg hover:underline">
          Cancel group
        </button>
      </div>
    </div>
  );
}

function Dag({
  graph,
  focalId,
}: {
  graph: ManifestGroupDependencyGraph;
  focalId: number;
}) {
  const navigate = useNavigate();
  const layout = useMemo(
    () =>
      computeDagLayout(
        graph.nodes.map((n) => ({ id: n.id, label: n.name, isHighlighted: n.isHighlighted })),
        graph.edges.map((e) => ({ fromId: e.fromId, toId: e.toId })),
      ),
    [graph],
  );

  // A lone focal node means the group has no cross-group dependencies.
  if (graph.nodes.length <= 1 && graph.edges.length === 0)
    return (
      <p className="text-muted text-sm">
        No cross-group dependencies. This group runs independently.
      </p>
    );

  return (
    <DagGraph
      layout={layout}
      onNodeClick={(id) => id !== focalId && navigate(`/groups/${id}`)}
    />
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-xs text-muted mb-1">{label}</label>
      {children}
    </div>
  );
}

function ErrorBox({ message }: { message: string }) {
  return (
    <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm">
      {message}
    </div>
  );
}
