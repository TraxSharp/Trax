import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useQuery } from "urql";
import { MACHINES, MACHINE_INSTANCES_ATTENTION, MACHINE_INSTANCE_STUCK_COUNTS } from "../graphql/queries";
import { BackLink, Loading } from "../components/detail";
import { MachineDiagram } from "../components/MachineDiagram";
import { WaitingOn } from "../components/WaitingOn";
import { TermHelp } from "../components/TermHelp";
import { formatTime } from "../lib/format";
import { countsByState } from "../lib/machineGraph";
import { MACHINE_INSTANCES_PATH, machineInstancePath, ownerLabel } from "../lib/machineInstances";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useUrlStateOrNull } from "../lib/urlState";
import type {
  MachineDefinition,
  MachineInstanceAttentionRow,
  MachineInstanceStuckCount,
  SnapshotOwnerKind,
} from "../types";

const PAGE_SIZE = 20;

// The API's count stops here (OperationsService.MachineInstanceCountCap); the counts stay exact.
const COUNT_CAP = 10_000;

interface MachinesData {
  operations: { machines: MachineDefinition[] };
}

interface CountsData {
  operations: { machineInstanceCounts: MachineInstanceStuckCount[] };
}

interface ListData {
  operations: {
    machineInstances: { items: MachineInstanceAttentionRow[]; totalCount: number; isCountCapped: boolean };
  };
}

const select = "text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

/**
 * One machine, at /machines/:name: its diagram, each state badged with how many instances are in it
 * and how many of those are stuck, and its instances under it. Clicking a state lists the instances
 * in it; the state, owner and stuck filters live in the URL. Never an instance's context, and never
 * the user who owns a draft.
 */
export function MachinePage() {
  const name = useParams().name ?? "";
  const navigate = useNavigate();
  const [state, setState] = useUrlStateOrNull("state");
  const [ownerKind, setOwnerKind] = useUrlStateOrNull<SnapshotOwnerKind>("owner");
  const [stuckParam, setStuck] = useUrlStateOrNull("stuck");
  const stuck = stuckParam === "true" ? true : null;
  const [skip, setSkip] = useState(0);

  const [machines] = useQuery<MachinesData>({ query: MACHINES });
  const [counts, reexecuteCounts] = useQuery<CountsData>({
    query: MACHINE_INSTANCE_STUCK_COUNTS,
    variables: { machine: name },
  });
  const [result, reexecute] = useQuery<ListData>({
    query: MACHINE_INSTANCES_ATTENTION,
    variables: { machine: name, state, ownerKind, stuck, skip, take: PAGE_SIZE },
  });
  const refetch = () => {
    reexecuteCounts({ requestPolicy: "network-only" });
    reexecute({ requestPolicy: "network-only" });
  };
  usePoll(refetch);
  // Instances move as the runs they invoke start, finish and are cancelled, and as their queued runs
  // are dispatched.
  useRefetchOnChange(["EXECUTION", "WORK_QUEUE"], refetch);

  const machine = machines.data?.operations?.machines.find((m) => m.name === name);
  const countRows = counts.data?.operations?.machineInstanceCounts;
  const byState = useMemo(() => countsByState(countRows ?? []), [countRows]);
  const drawn = machine != null && machine.graphUnavailableReason == null;
  // States an instance is in that the diagram does not draw: written under an older version, or every
  // state of a machine with no diagram.
  const undeclared = machine
    ? [...byState.entries()].filter(([s, c]) => c.count > 0 && !machine.states.some((d) => d.name === s))
    : [];

  const page = result.data?.operations?.machineInstances;
  const rows = page?.items ?? [];
  const total = page?.totalCount ?? 0;

  // A refresh can leave fewer instances than the page starts at: move back to the last page that
  // has rows. Set while rendering, as React adjusts state from what a render reads.
  const pastTheEnd = page != null && skip > 0 && skip >= total;
  if (pastTheEnd) setSkip(Math.max(0, Math.floor((total - 1) / PAGE_SIZE) * PAGE_SIZE));

  const filterTo = (next: { state?: string | null; ownerKind?: SnapshotOwnerKind | null; stuck?: boolean }) => {
    if (next.state !== undefined) setState(next.state);
    if (next.ownerKind !== undefined) setOwnerKind(next.ownerKind);
    if (next.stuck !== undefined) setStuck(next.stuck ? "true" : null);
    setSkip(0);
  };

  const back = <BackLink to={MACHINE_INSTANCES_PATH} label="All machines" />;

  if (machines.error)
    return (
      <>
        {back}
        <h1 className="text-2xl font-bold text-fg mt-2 mb-6">{name}</h1>
        <p className="text-danger-fg">{machines.error.message}</p>
      </>
    );
  if (!machines.data) return <Loading />;
  if (!machine)
    return (
      <>
        {back}
        <h1 className="text-2xl font-bold text-fg mt-2 mb-6">Machine not found</h1>
        <p className="text-muted">This host registers no machine named {name}.</p>
      </>
    );

  const totalInstances = [...byState.values()].reduce((n, c) => n + c.count, 0);
  const totalStuck = [...byState.values()].reduce((n, c) => n + c.stuck, 0);

  return (
    <div>
      {back}
      <div className="flex items-center gap-2 mt-2">
        <h1 className="text-2xl font-bold text-fg">{machine.name}</h1>
        <TermHelp term="machine" />
      </div>
      <p className="text-sm text-muted mb-6">
        Version {machine.version} · {machine.systemOwned ? "held by the system" : "held by users and the system"}
        {countRows && (
          <>
            {" "}
            · {totalInstances.toLocaleString("en-US")} {totalInstances === 1 ? "instance" : "instances"}
            {totalStuck > 0 && `, ${totalStuck.toLocaleString("en-US")} stuck`}
          </>
        )}
      </p>

      <section aria-label="States" className="bg-surface rounded-lg border border-line p-5 mb-6">
        <h2 className="text-sm font-semibold text-fg mb-3">States</h2>
        <MachineDiagram
          machine={machine}
          counts={byState}
          selected={state}
          onSelect={(s) => filterTo({ state: s === state ? null : s })}
        />
        {drawn && (
          <p className="mt-2 text-xs text-muted">Click a state to list the instances in it; click it again to list them all.</p>
        )}
        {undeclared.length > 0 && (
          <p className={`mt-2 text-xs ${drawn ? "text-warn-fg" : "text-fg-2"}`}>
            {drawn
              ? `Instances in states version ${machine.version} does not declare, written under an earlier version: `
              : "Instances by state: "}
            {undeclared.map(([s, c], i) => (
              <span key={s}>
                {i > 0 && ", "}
                <button
                  type="button"
                  aria-pressed={state === s}
                  onClick={() => filterTo({ state: s === state ? null : s })}
                  className={`underline ${state === s ? "font-semibold text-accent-fg" : ""}`}
                >
                  {s}
                </button>{" "}
                ({c.count.toLocaleString("en-US")}
                {c.stuck > 0 && `, ${c.stuck.toLocaleString("en-US")} stuck`})
              </span>
            ))}
            .
          </p>
        )}
        {counts.error && <p className="mt-2 text-sm text-danger-fg">{counts.error.message}</p>}
      </section>

      <section aria-label="Instances">
        <div className="flex flex-wrap items-end gap-3 mb-2">
          <h2 className="text-sm font-semibold text-fg mr-auto self-center">Instances</h2>
          <label className="text-xs text-muted">
            State
            <select
              aria-label="State"
              value={state ?? ""}
              onChange={(e) => filterTo({ state: e.target.value || null })}
              className={`${select} block mt-1`}
            >
              <option value="">Any state</option>
              {[...new Set([...machine.states.map((s) => s.name), ...undeclared.map(([s]) => s), ...(state ? [state] : [])])].map(
                (s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ),
              )}
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
          <label className="flex items-center gap-1.5 text-sm text-fg-2 pb-1">
            <input type="checkbox" checked={stuck === true} onChange={(e) => filterTo({ stuck: e.target.checked })} />
            Stuck only
          </label>
        </div>
        <p className="text-xs text-muted mb-2">
          Newest first, by when each instance was last written. An instance is stuck when its timer should have
          fired and has not, or when the run it waits on failed or was cancelled.
        </p>
        {page?.isCountCapped && (
          <p className="text-xs text-warn-fg mb-2">
            More than {COUNT_CAP.toLocaleString("en-US")} instances match, so the pager stops there. The counts on
            the diagram are exact; filter by state to reach the rest.
          </p>
        )}
        {result.error && (
          <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
            {result.error.message}
          </div>
        )}

        <div className="bg-surface rounded-lg border border-line overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-sunken text-muted text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Id</th>
                <th className="px-4 py-2 font-medium">Owner</th>
                <th className="px-4 py-2 font-medium">State</th>
                <th className="px-4 py-2 font-medium">Last changed</th>
                <th className="px-4 py-2 font-medium">Timer due</th>
                <th className="px-4 py-2 font-medium">Waiting on</th>
                <th className="px-4 py-2 font-medium">Attention</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {rows.map((i) => (
                <tr
                  key={i.rowId}
                  data-instance-id={i.id}
                  onClick={() => navigate(machineInstancePath(i))}
                  className="cursor-pointer hover:bg-hover"
                >
                  <td className="px-4 py-2 font-mono text-xs">
                    <Link
                      to={machineInstancePath(i)}
                      onClick={(e) => e.stopPropagation()}
                      className="text-accent-fg hover:underline"
                    >
                      {i.id}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-fg-2">{ownerLabel(i.ownerKind)}</td>
                  <td className="px-4 py-2 text-fg-2">{i.state}</td>
                  <td className="px-4 py-2 text-fg-2">{formatTime(i.inStateSince)}</td>
                  <td className="px-4 py-2 text-fg-2">
                    {i.dueAt ? (
                      <span className="flex items-center gap-1">
                        {formatTime(i.dueAt)}
                        {i.overdue && (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg">Overdue</span>
                        )}
                      </span>
                    ) : null}
                  </td>
                  <td className="px-4 py-2" onClick={(e) => e.stopPropagation()}>
                    <WaitingOn run={i.waitingOn} />
                  </td>
                  <td className="px-4 py-2">
                    {i.stuck && (
                      <span className="text-xs px-2 py-0.5 rounded-full bg-danger-soft text-danger-fg">Stuck</span>
                    )}
                  </td>
                </tr>
              ))}
              {page && rows.length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-8 text-center text-muted">
                    {state || ownerKind || stuck
                      ? "No instances match."
                      : `${machine.name} has no instances. An instance is listed here once something starts one.`}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        <div className="flex items-center justify-between mt-4 text-sm">
          <span className="text-muted">
            {page && total > 0 && !pastTheEnd
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
      </section>
    </div>
  );
}
