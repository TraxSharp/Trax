import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { TRAINS } from "../graphql/queries";
import { QueueTrainDialog } from "../components/QueueTrainDialog";
import { RunTrainDialog } from "../components/RunTrainDialog";
import type { TrainInfo } from "../types";

interface TrainsData {
  operations: { trains: TrainInfo[] };
}

type SortKey = "name" | "kind" | "lifetime" | "roles";
const SORT_VALUE: Record<SortKey, (t: TrainInfo) => string> = {
  name: (t) => shortName(t.fullName).toLowerCase(),
  kind: (t) => kind(t),
  lifetime: (t) => t.lifetime,
  roles: (t) => t.requiredRoles.join(", "),
};

export function TrainsPage() {
  const [hideAdmin, setHideAdmin] = useState(true);
  const [filter, setFilter] = useState("");
  const [sortKey, setSortKey] = useState<SortKey>("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  // The train a Queue or Run dialog is open for.
  const [dialog, setDialog] = useState<{ mode: "queue" | "run"; train: string } | null>(null);
  const [result] = useQuery<TrainsData>({
    query: TRAINS,
    variables: { hideAdmin },
  });

  // The train registry is a small in-memory list, so filter and sort client-side.
  const data = result.data?.operations.trains;
  const trains = useMemo(() => {
    const all = data ?? [];
    const filtered = filter
      ? all.filter((t) =>
          t.fullName.toLowerCase().includes(filter.toLowerCase()),
        )
      : all;
    const get = SORT_VALUE[sortKey];
    const dir = sortDir === "asc" ? 1 : -1;
    return [...filtered].sort((a, b) => get(a).localeCompare(get(b)) * dir);
  }, [data, filter, sortKey, sortDir]);

  function toggleSort(key: SortKey) {
    if (sortKey === key) setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    else {
      setSortKey(key);
      setSortDir("asc");
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-4">
        <h1 className="text-2xl font-bold text-fg">Trains</h1>
        <div className="flex items-center gap-4">
          <input
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filter by name…"
            className="text-sm border border-line-strong rounded-md px-2 py-1 w-56"
          />
          <label className="flex items-center gap-2 text-sm text-fg-2 whitespace-nowrap">
            <input
              type="checkbox"
              checked={hideAdmin}
              onChange={(e) => setHideAdmin(e.target.checked)}
            />
            Hide admin
          </label>
        </div>
      </div>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <SortHeader label="Train" col="name" {...{ sortKey, sortDir, toggleSort }} />
              <SortHeader label="Kind" col="kind" {...{ sortKey, sortDir, toggleSort }} />
              <SortHeader label="Lifetime" col="lifetime" {...{ sortKey, sortDir, toggleSort }} />
              <SortHeader label="Roles" col="roles" {...{ sortKey, sortDir, toggleSort }} />
              <th className="px-4 py-2 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {trains.map((t) => (
              <tr key={t.fullName}>
                <td className="px-4 py-2">
                  <Link
                    to={`/trains/${encodeURIComponent(t.fullName)}`}
                    className="font-medium text-accent-fg hover:underline"
                    title={t.fullName}
                  >
                    {shortName(t.fullName)}
                  </Link>
                </td>
                <td className="px-4 py-2 text-fg-2">{kind(t)}</td>
                <td className="px-4 py-2 text-fg-2">{t.lifetime}</td>
                <td className="px-4 py-2 text-fg-2">
                  {t.requiredRoles.length > 0 ? t.requiredRoles.join(", ") : "—"}
                </td>
                <td className="px-4 py-2 text-right whitespace-nowrap">
                  <button
                    aria-label={`Queue ${shortName(t.fullName)}`}
                    onClick={() => setDialog({ mode: "queue", train: t.fullName })}
                    className="text-xs px-2 py-1 rounded-md bg-accent text-on-accent hover:bg-accent-hover mr-2"
                  >
                    Queue
                  </button>
                  <button
                    aria-label={`Run ${shortName(t.fullName)}`}
                    onClick={() => setDialog({ mode: "run", train: t.fullName })}
                    className="text-xs px-2 py-1 rounded-md border border-line-strong text-fg-2 hover:bg-hover"
                  >
                    Run
                  </button>
                </td>
              </tr>
            ))}
            {trains.length === 0 && !result.fetching && (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-muted">
                  No trains registered.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {dialog?.mode === "queue" && (
        <QueueTrainDialog
          initialTrain={dialog.train}
          onQueued={() => {}}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.mode === "run" && (
        <RunTrainDialog initialTrain={dialog.train} onClose={() => setDialog(null)} />
      )}
    </div>
  );
}

function SortHeader({
  label,
  col,
  sortKey,
  sortDir,
  toggleSort,
}: {
  label: string;
  col: SortKey;
  sortKey: SortKey;
  sortDir: "asc" | "desc";
  toggleSort: (k: SortKey) => void;
}) {
  const active = sortKey === col;
  return (
    <th className="px-4 py-2 font-medium">
      <button
        onClick={() => toggleSort(col)}
        className="flex items-center gap-1 hover:text-fg"
      >
        {label}
        <span className={active ? "" : "opacity-0"}>
          {sortDir === "asc" ? "▲" : "▼"}
        </span>
      </button>
    </th>
  );
}

function kind(t: TrainInfo): string {
  if (t.isQuery) return "Query";
  if (t.isMutation) return "Mutation";
  return "—";
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
