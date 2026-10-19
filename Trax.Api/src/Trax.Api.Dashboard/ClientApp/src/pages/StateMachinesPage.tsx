import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { MACHINES, MACHINE_INSTANCE_STUCK_COUNTS } from "../graphql/queries";
import { machinePath } from "../lib/machineInstances";
import { usePoll } from "../lib/poll";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type { MachineDefinition, MachineInstanceStuckCount } from "../types";
import { TermHelp } from "../components/TermHelp";

interface MachinesData {
  operations: { machines: MachineDefinition[] };
}

interface CountsData {
  operations: { machineInstanceCounts: MachineInstanceStuckCount[] };
}

const DOCS = "https://traxsharp.net/docs/statemachine";

// Every machine the host registers (operations.machines), whether or not it has an instance, with
// how many instances it has and how many of them are stuck (operations.machineInstanceCounts). A
// machine's page holds its diagram and its instances.
export function StateMachinesPage() {
  const [machines] = useQuery<MachinesData>({ query: MACHINES });
  const [counts, reexecuteCounts] = useQuery<CountsData>({ query: MACHINE_INSTANCE_STUCK_COUNTS, variables: {} });
  // The definitions do not change while a host runs; the counts move with every instance.
  const refetch = () => reexecuteCounts({ requestPolicy: "network-only" });
  usePoll(refetch);
  useRefetchOnChange(["EXECUTION", "WORK_QUEUE"], refetch);

  const list = machines.data?.operations?.machines ?? [];
  const byMachine = new Map<string, { count: number; stuck: number }>();
  for (const c of counts.data?.operations?.machineInstanceCounts ?? []) {
    const sum = byMachine.get(c.machine) ?? { count: 0, stuck: 0 };
    sum.count += c.count;
    sum.stuck += c.stuck;
    byMachine.set(c.machine, sum);
  }

  return (
    <div>
      <div className="flex items-center gap-2">
        <h1 className="text-2xl font-bold text-fg mb-1">Machines</h1>
        <TermHelp term="machine" />
      </div>
      <p className="text-sm text-muted mb-6">
        Every state machine this host registers, with how many instances each has and how many of them are stuck: a
        timer that should have fired, or a run they wait on that failed or was cancelled. Open one to see its states
        and its instances.
      </p>

      {machines.error ? (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {machines.error.message}
        </div>
      ) : (
        <section aria-label="Registered machines" className="bg-surface rounded-lg border border-line overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-sunken text-muted text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Machine</th>
                <th className="px-4 py-2 font-medium">Version</th>
                <th className="px-4 py-2 font-medium">Instances are held by</th>
                <th className="px-4 py-2 font-medium text-right">States</th>
                <th className="px-4 py-2 font-medium text-right">Instances</th>
                <th className="px-4 py-2 font-medium text-right">Stuck</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {list.map((m) => {
                const c = byMachine.get(m.name) ?? { count: 0, stuck: 0 };
                return (
                  <tr key={m.name} data-machine={m.name}>
                    <td className="px-4 py-2">
                      <Link to={machinePath(m.name)} className="text-accent-fg hover:underline font-medium">
                        {m.name}
                      </Link>
                    </td>
                    <td className="px-4 py-2 text-fg-2">{m.version}</td>
                    <td className="px-4 py-2 text-fg-2">{m.systemOwned ? "The system" : "Users and the system"}</td>
                    <td className="px-4 py-2 text-fg-2 text-right">
                      {m.graphUnavailableReason != null ? (
                        <span className="text-muted" title="This machine has no diagram">
                          —
                        </span>
                      ) : (
                        m.states.length
                      )}
                    </td>
                    <td className="px-4 py-2 text-fg-2 text-right">
                      {counts.data ? c.count.toLocaleString("en-US") : ""}
                    </td>
                    <td className="px-4 py-2 text-right">
                      {c.stuck > 0 ? (
                        <Link
                          to={`${machinePath(m.name)}?stuck=true`}
                          className="text-xs px-2 py-0.5 rounded-full bg-danger-soft text-danger-fg hover:underline"
                          aria-label={`${c.stuck} stuck`}
                        >
                          {c.stuck.toLocaleString("en-US")}
                        </Link>
                      ) : counts.data ? (
                        <span className="text-muted">0</span>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
              {machines.data && list.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-muted">
                    This host registers no state machines. A machine registered with the host is listed here, with
                    or without instances.{" "}
                    <a href={DOCS} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
                      State machines
                    </a>
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </section>
      )}
      {counts.error && <p className="text-sm text-danger-fg mt-2">{counts.error.message}</p>}
    </div>
  );
}
