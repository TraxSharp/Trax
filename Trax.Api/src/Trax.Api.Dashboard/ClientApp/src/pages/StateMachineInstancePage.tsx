import { useEffect, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useSubscription } from "urql";
import { MACHINES, MACHINE_INSTANCE } from "../graphql/queries";
import { CANCEL_MACHINE_INSTANCE } from "../graphql/mutations";
import { ON_MACHINE_INSTANCE_CHANGED } from "../graphql/subscriptions";
import { StateBadge } from "../components/StateBadge";
import { BackLink, DetailLink, DetailPanel, Fields, Loading } from "../components/detail";
import { MachineDiagram } from "../components/MachineDiagram";
import { WaitingOn } from "../components/WaitingOn";
import { connection } from "../lib/connection";
import { enumLabel, formatTime, shortName } from "../lib/format";
import { delayWords, outcomeOf } from "../lib/machineGraph";
import { machinePath, parseOwnerSegment } from "../lib/machineInstances";
import { usePoll } from "../lib/poll";
import { toast } from "../lib/toast";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import type {
  MachineDefinition,
  MachineInstanceCancelResponse,
  MachineInstanceChange,
  MachineInstanceDetail,
} from "../types";
import { confirmAction } from "../lib/confirm";
import { TransitionHistory } from "../components/TransitionHistory";

interface InstanceData {
  operations: { machineInstance: MachineInstanceDetail | null };
}

interface MachinesData {
  operations: { machines: MachineDefinition[] };
}

interface CancelData {
  operations: { cancelMachineInstance: MachineInstanceCancelResponse };
}

interface FeedData {
  onMachineInstanceChanged: MachineInstanceChange;
}

interface FeedState {
  live: MachineInstanceChange | null;
  seen: number;
}

/**
 * One state-machine instance, at /machines/:machine/:owner/:id, with ?row= for a user's draft
 * (operations.machineInstance). It draws the machine with the state the instance is in lit, lists
 * the commands that state accepts (read-only: the console never sends one), its timer and the run
 * it waits on, and the runs it invoked. It follows the instance live through
 * onMachineInstanceChanged and polls only while that feed is not connected. Never its context, and
 * never the user who owns it. An operator can cancel a system-owned instance's live run
 * (cancelMachineInstance); the API refuses the rest with its reason.
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
        ? "A user's draft is named by its row as well as its id, because several users can each hold a draft under one id. Open it from its machine's page."
        : null;

  const [result, reexecute] = useQuery<InstanceData>({
    query: MACHINE_INSTANCE,
    variables: { machine, ownerKind, id, rowId },
    pause: refusal != null,
  });
  const [machines] = useQuery<MachinesData>({ query: MACHINES, pause: refusal != null });
  // reexecute ignores pause, so a route the API cannot look up must not refetch at all: every
  // change would send a read that can only fail.
  const refetch = () => {
    if (refusal == null) reexecute({ requestPolicy: "network-only" });
  };

  // The instance as the feed last wrote it. The feed carries its state and when it was written,
  // so the lit state moves at once; the rest (its timer, its run) is read again. The reducer keeps
  // only this instance's events and the reloads, so another instance's write changes nothing.
  const [{ data: feed }] = useSubscription<FeedData, FeedState>(
    { query: ON_MACHINE_INSTANCE_CHANGED, variables: { machine }, pause: refusal != null },
    (prev, next) => {
      const e = next.onMachineInstanceChanged;
      const ours = e.id === id && e.ownerKind === ownerKind && (rowId == null || e.rowId === rowId);
      if (!ours && !e.reload) return prev ?? { live: null, seen: 0 };
      return { live: ours ? e : (prev?.live ?? null), seen: (prev?.seen ?? 0) + 1 };
    },
  );
  const live = feed?.live ?? null;
  const refetchRef = useRef(refetch);
  useEffect(() => {
    refetchRef.current = refetch;
  });
  useEffect(() => {
    if (feed && feed.seen > 0) refetchRef.current();
  }, [feed]);

  // The feed is the instance's own change signal; polling stands in only while it is not connected.
  usePoll(() => {
    if (connection.get() !== "connected") refetch();
  });
  // Its run moves with the work queue and the runs themselves, which do not write the instance.
  useRefetchOnChange(["EXECUTION", "WORK_QUEUE"], refetch);
  const [, cancelInstance] = useMutation<CancelData>(CANCEL_MACHINE_INSTANCE);
  const [cancelling, setCancelling] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const instance = result.data?.operations?.machineInstance;
  const definition = machines.data?.operations?.machines.find((m) => m.name === machine);
  // The feed's write is newer than the read until the read catches up.
  const fresher = live != null && instance != null && Date.parse(live.updatedAt) > Date.parse(instance.updatedAt);
  const state = fresher ? live.state : instance?.state;

  async function onCancel() {
    if (!instance || ownerKind == null || cancelling) return;
    if (
      !(await confirmAction({
        title: "Cancel the run it waits on",
        message: `Cancel the train run ${instance.machine}'s '${instance.state}' state waits on? A queued run never starts; a running one stops at its next junction. The instance then moves through the state's OnCancelled edge.`,
        danger: true,
      }))
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
      {instance && instance.ownerKind === "SYSTEM" && instance.hasLiveInvokedRun && !live?.deleted && (
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

  const back = <BackLink to={machinePath(machine)} label={`All instances of ${machine}`} />;

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

  const leaving = definition?.transitions.filter((t) => t.from === state) ?? [];
  const commands = leaving.filter((t) => t.kind === "COMMAND");
  const timers = leaving.filter((t) => t.kind === "TIMER");
  const outcomes = leaving.filter((t) => t.kind === "OUTCOME");
  const runs = instance.invokedRuns;
  // While the feed is ahead of the read, the timer and run belong to the state it left.
  const settled = !fresher;

  return (
    <div>
      {back}
      {header(instance.machine)}

      {live?.deleted && (
        <div className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-4 text-sm mb-6">
          This instance was deleted or has expired. What is shown is how it was last seen.
        </div>
      )}
      {actionError && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-6">
          {actionError}
        </div>
      )}

      <section aria-label="Where it is" className="bg-surface rounded-lg border border-line p-5 mb-6">
        <h2 className="text-sm font-semibold text-fg mb-3">Where it is</h2>
        {definition ? (
          <MachineDiagram machine={definition} current={state} />
        ) : machines.error ? (
          <p className="text-sm text-danger-fg">{machines.error.message}</p>
        ) : machines.data ? (
          <p className="text-sm text-muted">This host no longer registers {machine}, so its states cannot be drawn.</p>
        ) : null}
        <div className="mt-4">
          <Fields
            rows={[
              [
                "State",
                <span className="text-xs px-2 py-0.5 rounded-full bg-accent-soft text-accent-fg">{state}</span>,
              ],
              ["Last changed", formatTime(fresher ? live.updatedAt : instance.inStateSince)],
              [
                "Timer due",
                settled && instance.dueAt ? (
                  <span className="inline-flex items-center gap-1">
                    {formatTime(instance.dueAt)}
                    {instance.overdue && (
                      <span className="text-xs px-2 py-0.5 rounded-full bg-warn-soft text-warn-fg">Overdue</span>
                    )}
                  </span>
                ) : (
                  "No timer in this state"
                ),
              ],
              [
                "Waiting on",
                settled && instance.waitingOn ? <WaitingOn run={instance.waitingOn} /> : "No run",
              ],
              [
                "Stuck",
                settled && instance.stuck ? (
                  <span className="text-xs px-2 py-0.5 rounded-full bg-danger-soft text-danger-fg">
                    {instance.overdue ? "Yes: its timer should have fired" : "Yes: the run it waits on ended"}
                  </span>
                ) : (
                  "No"
                ),
              ],
            ]}
          />
        </div>
      </section>

      {definition && definition.graphUnavailableReason == null && (
        <section aria-label="What moves it on" className="bg-surface rounded-lg border border-line p-5 mb-6">
          <h2 className="text-sm font-semibold text-fg mb-1">Commands it accepts</h2>
          <p className="text-xs text-muted mb-3">
            What the application that owns this machine can send it in {state}. The console shows them and sends
            none.
          </p>
          {commands.length === 0 ? (
            <p className="text-sm text-muted">No command moves it on from {state}.</p>
          ) : (
            <ul aria-label="Commands it accepts" className="space-y-1 text-sm">
              {commands.map((t) => (
                <li key={`${t.command}|${t.to}`}>
                  <span className="font-mono text-fg">{t.command}</span>
                  <span className="text-muted"> moves it to </span>
                  <span className="text-fg">{t.to}</span>
                  {t.guard && <span className="text-muted"> when: {t.guard}</span>}
                </li>
              ))}
            </ul>
          )}
          {(timers.length > 0 || outcomes.length > 0) && (
            <ul aria-label="What else moves it on" className="mt-3 space-y-1 text-sm text-fg-2">
              {timers.map((t) => (
                <li key={`${t.command}|${t.to}`}>
                  After {delayWords(t.after)} in {state} it moves to {t.to}
                  {settled && instance.dueAt && <>, at {formatTime(instance.dueAt)}</>}.
                </li>
              ))}
              {outcomes.map((t) => (
                <li key={`${t.command}|${t.to}`}>
                  When the run {state} started is {outcomeOf(t)}, it moves to {t.to}.
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {/* Every transition it made, newest first; read again whenever its revision moves. */}
      {ownerKind && (
        <TransitionHistory
          key={instance.updatedAt}
          instance={{ machine: instance.machine, ownerKind, id: instance.id, rowId: instance.rowId }}
        />
      )}

      {/* No context: an operator sees where an instance is, never what it holds. */}
      <DetailPanel title="Details">
        <Fields
          rows={[
            [
              "Machine",
              <Link to={machinePath(instance.machine)} className="text-accent-fg hover:underline">
                {instance.machine}
              </Link>,
            ],
            ["Id", instance.id],
            ["Owner", instance.ownerKind === "SYSTEM" ? "System" : "A user"],
            ["Row", instance.rowId],
            ["Machine version", instance.version],
            ["Created at", formatTime(instance.createdAt, "Not recorded")],
            ["Updated at", formatTime(fresher ? live.updatedAt : instance.updatedAt)],
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
            <DetailLink to={`/runs/queued/${instance.queuedInvokedRunEntryId}`}>
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
                      <DetailLink to={`/runs/${r.id}`}>{r.id}</DetailLink>
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
