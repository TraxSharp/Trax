import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useQuery } from "urql";
import { MACHINE_INSTANCES, MACHINE_INSTANCE_COUNTS } from "../graphql/queries";
import { formatTime } from "../lib/format";
import { machineInstancePath, ownerLabel } from "../lib/machineInstances";
import { usePoll } from "../lib/poll";
import type { MachineInstance, MachineInstanceCount, SnapshotOwnerKind } from "../types";

const PAGE_SIZE = 20;

// The API's count stops here (OperationsService.MachineInstanceCountCap); machineInstanceCounts
// stays exact.
const COUNT_CAP = 10_000;

interface CountsData {
  operations: { machineInstanceCounts: MachineInstanceCount[] };
}

interface ListData {
  operations: {
    machineInstances: { items: MachineInstance[]; totalCount: number; isCountCapped: boolean };
  };
}

const select = "text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

// The operator's read-only view of state-machine instances (operations.machineInstanceCounts and
// operations.machineInstances), as the Blazor StateMachinesPage shows them: where each instance is
// and when it got there, and whether it waits on a train run it invoked. Never its context, and
// never the user who owns a draft. The list pages by offset: it is ordered by a time that moves.
export function StateMachinesPage() {
  const navigate = useNavigate();
  const [machine, setMachine] = useState<string | null>(null);
  const [state, setState] = useState<string | null>(null);
  const [ownerKind, setOwnerKind] = useState<SnapshotOwnerKind | null>(null);
  const [skip, setSkip] = useState(0);

  const [counts, reexecuteCounts] = useQuery<CountsData>({ query: MACHINE_INSTANCE_COUNTS });
  const [result, reexecute] = useQuery<ListData>({
    query: MACHINE_INSTANCES,
    variables: { machine, state, ownerKind, skip, take: PAGE_SIZE },
  });
  usePoll(() => {
    reexecuteCounts({ requestPolicy: "network-only" });
    reexecute({ requestPolicy: "network-only" });
  });

  const countRows = counts.data?.operations?.machineInstanceCounts ?? [];
  const machines = [...new Set(countRows.map((c) => c.machine))];
  const states = [
    ...new Set(countRows.filter((c) => machine == null || c.machine === machine).map((c) => c.state)),
  ].sort();
  const page = result.data?.operations?.machineInstances;
  const rows = page?.items ?? [];
  const total = page?.totalCount ?? 0;

  // A changed filter starts again from the first page.
  const filterTo = (next: { machine?: string | null; state?: string | null; ownerKind?: SnapshotOwnerKind | null }) => {
    if (next.machine !== undefined) setMachine(next.machine);
    if (next.state !== undefined) setState(next.state);
    if (next.ownerKind !== undefined) setOwnerKind(next.ownerKind);
    setSkip(0);
  };

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-1">State machines</h1>
      <p className="text-sm text-muted mb-6">
        The instances of every state machine this host persists, system-owned and users' drafts alike:
        which state each is in, when it got there, and whether it waits on a train run it invoked. An
        instance's context is never shown.
      </p>

      <section aria-label="Instances by state" className="bg-surface rounded-lg border border-line p-5 mb-6">
        <h2 className="text-sm font-semibold text-fg mb-3">Instances by state</h2>
        {counts.error ? (
          <p className="text-sm text-danger-fg">{counts.error.message}</p>
        ) : counts.data && countRows.length === 0 ? (
          <p className="text-sm text-muted">No state machine instances.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-muted text-left">
              <tr>
                <th className="py-1 font-medium">Machine</th>
                <th className="py-1 font-medium">State</th>
                <th className="py-1 font-medium">Owner</th>
                <th className="py-1 font-medium text-right">Instances</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {countRows.map((c) => (
                <tr key={`${c.machine}|${c.state}|${c.ownerKind}`}>
                  <td className="py-1">
                    <button
                      onClick={() => filterTo({ machine: c.machine, state: c.state, ownerKind: c.ownerKind })}
                      title="List these instances"
                      className="text-accent-fg hover:underline text-left"
                    >
                      {c.machine}
                    </button>
                  </td>
                  <td className="py-1 text-fg-2">{c.state}</td>
                  <td className="py-1 text-fg-2">{ownerLabel(c.ownerKind)}</td>
                  <td className="py-1 text-fg-2 text-right">{c.count.toLocaleString("en-US")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <div className="flex flex-wrap items-end gap-3 mb-2">
        <label className="text-xs text-muted">
          Machine
          <select
            aria-label="Machine"
            value={machine ?? ""}
            onChange={(e) => filterTo({ machine: e.target.value || null, state: null })}
            className={`${select} block mt-1`}
          >
            <option value="">Any machine</option>
            {machines.map((m) => (
              <option key={m} value={m}>
                {m}
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs text-muted">
          State
          <select
            aria-label="State"
            value={state ?? ""}
            onChange={(e) => filterTo({ state: e.target.value || null })}
            className={`${select} block mt-1`}
          >
            <option value="">Any state</option>
            {states.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs text-muted">
          Owner
          <select
            aria-label="Owner"
            value={ownerKind ?? ""}
            onChange={(e) => filterTo({ ownerKind: (e.target.value || null) as SnapshotOwnerKind | null })}
            className={`${select} block mt-1`}
          >
            <option value="">Users and system</option>
            <option value="SYSTEM">System</option>
            <option value="USER">User</option>
          </select>
        </label>
      </div>
      <p className="text-xs text-muted mb-2">
        Newest first, by when each instance was last written. Click a machine above to list its
        instances in that state.
      </p>
      {page?.isCountCapped && (
        <p className="text-xs text-warn-fg mb-2">
          More than {COUNT_CAP.toLocaleString("en-US")} instances match, so the pager stops there. The
          counts above are exact; filter by machine and state to reach the rest.
        </p>
      )}

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      <div className="bg-surface rounded-lg border border-line overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Machine</th>
              <th className="px-4 py-2 font-medium">Owner</th>
              <th className="px-4 py-2 font-medium">Id</th>
              <th className="px-4 py-2 font-medium">State</th>
              <th className="px-4 py-2 font-medium">Waiting on a run</th>
              <th className="px-4 py-2 font-medium">Updated</th>
              <th className="px-4 py-2 font-medium">Created</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((i) => (
              <tr
                key={i.rowId}
                onClick={() => navigate(machineInstancePath(i))}
                className="cursor-pointer hover:bg-hover"
              >
                <td className="px-4 py-2 text-fg">{i.machine}</td>
                <td className="px-4 py-2 text-fg-2">{ownerLabel(i.ownerKind)}</td>
                <td className="px-4 py-2 font-mono text-xs">
                  <Link
                    to={machineInstancePath(i)}
                    onClick={(e) => e.stopPropagation()}
                    className="text-accent-fg hover:underline"
                  >
                    {i.id}
                  </Link>
                </td>
                <td className="px-4 py-2 text-fg-2">{i.state}</td>
                <td className="px-4 py-2">
                  {i.hasLiveInvokedRun && (
                    <span className="text-xs px-2 py-0.5 rounded-full bg-info-soft text-info-fg">Yes</span>
                  )}
                </td>
                <td className="px-4 py-2 text-fg-2">{formatTime(i.updatedAt)}</td>
                <td className="px-4 py-2 text-fg-2">{formatTime(i.createdAt)}</td>
              </tr>
            ))}
            {page && rows.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-muted">
                  No instances match.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between mt-4 text-sm">
        <span className="text-muted">
          {page && total > 0
            ? `${skip + 1}–${Math.min(skip + rows.length, total)} of ${total.toLocaleString("en-US")}${page.isCountCapped ? "+" : ""}`
            : page
              ? "0 total"
              : ""}
        </span>
        <div className="flex gap-2">
          <button
            onClick={() => setSkip((s) => Math.max(0, s - PAGE_SIZE))}
            disabled={skip === 0}
            className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
          >
            Previous
          </button>
          <button
            onClick={() => setSkip((s) => s + PAGE_SIZE)}
            disabled={skip + PAGE_SIZE >= total}
            className="px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 disabled:opacity-40 hover:bg-hover"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
