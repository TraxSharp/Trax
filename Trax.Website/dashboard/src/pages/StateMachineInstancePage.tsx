import { useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { MACHINE_INSTANCE } from "../graphql/queries";
import { CANCEL_MACHINE_INSTANCE } from "../graphql/mutations";
import { StateBadge } from "../components/StateBadge";
import { BackLink, DetailLink, DetailPanel, Fields, Loading } from "../components/detail";
import { enumLabel, formatTime, shortName } from "../lib/format";
import { MACHINE_INSTANCES_PATH, parseOwnerSegment } from "../lib/machineInstances";
import { usePoll } from "../lib/poll";
import { toast } from "../lib/toast";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type { MachineInstanceCancelResponse, MachineInstanceDetail } from "../types";

interface InstanceData {
  operations: { machineInstance: MachineInstanceDetail | null };
}

interface CancelData {
  operations: { cancelMachineInstance: MachineInstanceCancelResponse };
}

/**
 * One state-machine instance, at /state-machines/:machine/:owner/:id, with ?row= for a user's
 * draft (operations.machineInstance). It shows the instance's state, version, timestamps, owner
 * kind and the train runs it invoked; never its context, and never the user who owns it. An
 * operator can cancel a system-owned instance's live run (cancelMachineInstance); the API refuses
 * the rest with its reason. Mirrors the Blazor StateMachineInstancePage.
 */
export function StateMachineInstancePage() {
  const params = useParams();
  const [search] = useSearchParams();
  const machine = params.machine ?? "";
  const id = params.id ?? "";
  const ownerKind = parseOwnerSegment(params.owner);
  const rowParam = search.get("row");
  const rowId = rowParam != null && /^\d+$/.test(rowParam) ? Number(rowParam) : null;

  // Why the route names no instance the API can look up, shown instead of "not found".
  const refusal =
    ownerKind == null
      ? `'${params.owner}' is not an owner: an instance is owned by the system or by a user.`
      : ownerKind === "USER" && rowId == null
        ? "A user's draft is named by its row as well as its id, because several users can each hold a draft under one id. Open it from the State machines list."
        : null;

  const [result, reexecute] = useQuery<InstanceData>({
    query: MACHINE_INSTANCE,
    variables: { machine, ownerKind, id, rowId },
    pause: refusal != null,
  });
  const refetch = () => reexecute({ requestPolicy: "network-only" });
  usePoll(refetch);
  // Its runs move with the work queue and the runs themselves.
  useRefetchOnChange(["EXECUTION", "WORK_QUEUE"], refetch);
  const [, cancelInstance] = useMutation<CancelData>(CANCEL_MACHINE_INSTANCE);
  const [cancelling, setCancelling] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const instance = result.data?.operations?.machineInstance;

  async function onCancel() {
    if (!instance || ownerKind == null || cancelling) return;
    if (
      !confirm(
        `Cancel the train run ${instance.machine}'s '${instance.state}' state waits on? A queued run never starts; a running one stops at its next junction. The instance then moves through the state's OnCancelled edge.`,
      )
    )
      return;
    setActionError(null);
    setCancelling(true);
    const r = await cancelInstance({ machine, ownerKind, id });
    setCancelling(false);
    const res = r.data?.operations?.cancelMachineInstance;
    if (r.error) setActionError(r.error.message);
    else if (!res?.success) setActionError(res?.message ?? "Could not cancel.");
    else {
      toast(res.message, "success");
      refetch();
    }
  }

  const header = (title: string) => (
    <div className="flex items-center gap-3 mt-2 mb-6">
      <h1 className="text-2xl font-bold text-fg">{title}</h1>
      {/* Operators cancel only a system-owned instance whose state waits on a run. */}
      {instance && instance.ownerKind === "SYSTEM" && instance.hasLiveInvokedRun && (
        <button
          onClick={onCancel}
          disabled={cancelling}
          className="ml-auto text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
        >
          Cancel
        </button>
      )}
    </div>
  );

  const back = <BackLink to={MACHINE_INSTANCES_PATH} label="All state machines" />;

  if (refusal)
    return (
      <>
        {back}
        {header("Could not load")}
        <div className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-4 text-sm">{refusal}</div>
      </>
    );
  if (result.error && !instance)
    return (
      <>
        {back}
        {header("Could not load")}
        <p className="text-danger-fg">{result.error.message}</p>
      </>
    );
  if (result.fetching && !instance) return <Loading />;
  if (!instance)
    return (
      <>
        {back}
        {header("Instance not found")}
        <p className="text-muted">
          No {params.owner} instance of {machine} has id {id}.
        </p>
      </>
    );

  const runs = instance.invokedRuns;
  return (
    <div>
      {back}
      {header(instance.machine)}

      {actionError && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-6">
          {actionError}
        </div>
      )}

      {/* No context: an operator sees where an instance is, never what it holds. */}
      <DetailPanel title="Details">
        <Fields
          rows={[
            ["Machine", instance.machine],
            ["Id", instance.id],
            ["Owner", instance.ownerKind === "SYSTEM" ? "System" : "A user"],
            ["Row", instance.rowId],
            [
              "State",
              <span className="text-xs px-2 py-0.5 rounded-full bg-info-soft text-info-fg">{instance.state}</span>,
            ],
            ["Machine version", instance.version],
            [
              "Waiting on a run",
              instance.hasLiveInvokedRun ? "Yes: its state invoked a train run and waits for its outcome" : "No",
            ],
            ["Created at", formatTime(instance.createdAt, "Not recorded")],
            ["Updated at", formatTime(instance.updatedAt)],
          ]}
        />
      </DetailPanel>

      <section aria-label="Invoked runs" className="bg-surface rounded-lg border border-line p-5 mb-6">
        <h2 className="text-sm font-semibold text-fg mb-3">Invoked runs</h2>
        {instance.ownerKind === "USER" && (
          <p className="text-xs text-muted mb-2">
            A user's draft lists only the run its state waits on: a run does not record which user's
            draft queued it, and several users can each hold a draft under one id.
          </p>
        )}
        {instance.queuedInvokedRunEntryId != null && (
          <p className="text-sm text-fg-2 mb-2">
            The run its state waits on is still queued:{" "}
            <DetailLink to={`/work-queue/${instance.queuedInvokedRunEntryId}`}>
              work queue entry {instance.queuedInvokedRunEntryId}
            </DetailLink>
            .
          </p>
        )}
        {runs.length === 0 ? (
          <p className="text-sm text-muted">No runs to show.</p>
        ) : (
          <>
            <table className="w-full text-sm">
              <thead className="text-muted text-left">
                <tr>
                  <th className="py-1 font-medium">Id</th>
                  <th className="py-1 font-medium">Train</th>
                  <th className="py-1 font-medium">State</th>
                  <th className="py-1 font-medium">Start time</th>
                  <th className="py-1 font-medium">End time</th>
                  <th className="py-1 font-medium">Failure class</th>
                  <th className="py-1 font-medium">External id</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {runs.map((r) => (
                  <tr key={r.id} data-run-id={r.id}>
                    <td className="py-1">
                      <DetailLink to={`/executions/${r.id}`}>{r.id}</DetailLink>
                    </td>
                    <td className="py-1 text-fg-2" title={r.name}>
                      {shortName(r.name)}
                    </td>
                    <td className="py-1">
                      <span className="flex items-center gap-1">
                        <StateBadge state={r.trainState} />
                        {r.isLive && (
                          <span className="text-xs px-2 py-0.5 rounded-full border border-info-line text-info-fg">Live</span>
                        )}
                        {r.cancellationRequested && (
                          <span className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg">
                            Cancellation requested
                          </span>
                        )}
                      </span>
                    </td>
                    <td className="py-1 text-fg-2">{formatTime(r.startTime)}</td>
                    <td className="py-1 text-fg-2">{formatTime(r.endTime)}</td>
                    <td className="py-1 text-fg-2">{enumLabel(r.failureClass)}</td>
                    <td className="py-1 text-fg-2 font-mono text-xs">{r.externalId}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {instance.isInvokedRunsCapped && (
              <p className="text-xs text-muted mt-2">
                Showing the newest {runs.length} runs; the instance invoked more.
              </p>
            )}
          </>
        )}
      </section>
    </div>
  );
}
