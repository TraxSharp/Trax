import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import {
  DEAD_LETTER_DETAIL,
  EXECUTIONS,
  EXECUTION_DETAIL,
  MANIFEST_DETAIL,
} from "../graphql/queries";
import { ACKNOWLEDGE_DEAD_LETTER, REQUEUE_DEAD_LETTER } from "../graphql/mutations";
import {
  BackLink,
  DetailLink,
  DetailPanel,
  Fields,
  Loading,
  NotFound,
} from "../components/detail";
import { NotePrompt } from "../components/NotePrompt";
import { Pager } from "../components/Pager";
import { StateBadge } from "../components/StateBadge";
import { enumLabel, formatMs, formatTime, prettyJson, shortName } from "../lib/format";
import { useKeyset } from "../lib/useKeyset";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { toast } from "../lib/toast";
import type {
  DeadLetterSummary,
  ExecutionDetail,
  ExecutionSummary,
  ManifestDetail,
  PagedResult,
} from "../types";

const FAILED_PAGE_SIZE = 10;

interface DlData {
  operations: { deadLetters: { deadLetter: DeadLetterSummary | null } };
}

// One dead letter: its details, the manifest it belongs to (properties masked by the API), the
// manifest's most recent failed run in full, and the manifest's failed runs. Mirrors the Blazor
// DeadLetterDetailPage, composed from deadLetter + manifestDetail + executions + executionDetail.
export function DeadLetterDetailPage() {
  const id = Number(useParams().id);
  const navigate = useNavigate();
  const [result, reexecute] = useQuery<DlData>({
    query: DEAD_LETTER_DETAIL,
    variables: { id },
  });
  useRefetchOnChange("DEAD_LETTER", () => reexecute({ requestPolicy: "network-only" }));
  const [, requeue] = useMutation(REQUEUE_DEAD_LETTER);
  const [, acknowledge] = useMutation(ACKNOWLEDGE_DEAD_LETTER);
  const [busy, setBusy] = useState(false);
  const [acking, setAcking] = useState(false);
  const d = result.data?.operations?.deadLetters?.deadLetter;

  const [manifestResult] = useQuery<{ operations: { manifestDetail: ManifestDetail | null } }>({
    query: MANIFEST_DETAIL,
    variables: { id: d?.manifestId ?? 0 },
    pause: d == null,
  });
  const manifest = manifestResult.data?.operations?.manifestDetail ?? null;

  if (result.error && !d)
    return (
      <>
        <BackLink to="/dead-letters" label="Dead letters" />
        <p className="text-danger-fg mt-4">{result.error.message}</p>
      </>
    );
  if (result.fetching && !d) return <Loading />;
  if (!d) return <NotFound what="dead letter" />;

  const refetch = () => reexecute({ requestPolicy: "network-only" });
  const awaiting = d.status === "AWAITING_INTERVENTION";
  const title = manifest ? shortName(manifest.name) : d.manifestName || `Dead letter #${d.id}`;

  // A re-queue opens the new work queue entry, as the Blazor page does. askAfresh: the run asks
  // the model afresh instead of replaying the failed run's decisions.
  async function onRequeue(askAfresh: boolean) {
    if (!confirm(askAfresh ? `Requeue dead letter #${id}, asking afresh?` : `Requeue dead letter #${id}?`))
      return;
    setBusy(true);
    const r = await requeue({ id, askAfresh });
    setBusy(false);
    const res = r.data?.operations?.deadLetters?.requeueDeadLetter;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) {
      toast(`${title} has been re-queued (work queue entry ${res.workQueueId}).`, "success");
      if (res.workQueueId != null) {
        navigate(`/work-queue/${res.workQueueId}`);
        return;
      }
    } else toast(res?.message ?? "Could not requeue.", "error");
    refetch();
  }

  async function onAck(note: string) {
    setBusy(true);
    const r = await acknowledge({ id, note });
    setBusy(false);
    setAcking(false);
    const res = r.data?.operations?.deadLetters?.acknowledgeDeadLetter;
    if (r.error) toast(r.error.message, "error");
    else if (res?.success) toast(`Dead letter #${id} has been acknowledged.`, "success");
    else toast(res?.message ?? "Could not acknowledge.", "error");
    refetch();
  }

  return (
    <div>
      <BackLink to="/dead-letters" label="Dead letters" />
      <div className="flex items-center gap-3 mt-2 mb-6">
        <h1 className="text-2xl font-bold text-fg">{title}</h1>
        {awaiting && (
          <div className="ml-auto flex gap-2">
            <button
              onClick={() => onRequeue(false)}
              disabled={busy}
              className="text-sm px-3 py-1 rounded-md bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
            >
              Requeue
            </button>
            <button
              onClick={() => onRequeue(true)}
              disabled={busy}
              title="Re-queue; the run asks the model afresh instead of replaying the failed run's decisions."
              className="text-sm px-3 py-1 rounded-md border border-accent-line text-accent-fg hover:bg-accent-soft disabled:opacity-50"
            >
              Requeue, ask afresh
            </button>
            <button
              onClick={() => setAcking(true)}
              disabled={busy}
              className="text-sm px-3 py-1 rounded-md border border-line-strong text-fg-2 hover:bg-hover disabled:opacity-50"
            >
              Acknowledge
            </button>
          </div>
        )}
      </div>

      <DetailPanel title="Dead letter details">
        <Fields
          rows={[
            ["ID", d.id],
            ["Status", enumLabel(d.status)],
            ["Dead-lettered", formatTime(d.deadLetteredAt)],
            ["Retry count at dead letter", d.retryCountAtDeadLetter],
            ["Resolved", formatTime(d.resolvedAt)],
            ["Resolution note", d.resolutionNote ?? "—"],
            [
              "Manifest ID",
              <span>
                <DetailLink to={`/manifests/${d.manifestId}`}>{d.manifestId}</DetailLink>
                {" · "}
                <DetailLink to={`/dead-letters?manifestId=${d.manifestId}`}>its dead letters</DetailLink>
              </span>,
            ],
            [
              "Retry execution",
              d.retryMetadataId != null ? (
                <DetailLink to={`/executions/${d.retryMetadataId}`}>#{d.retryMetadataId}</DetailLink>
              ) : (
                "—"
              ),
            ],
          ]}
        />
        {d.reason && (
          <div className="mt-3">
            <p className="text-xs text-muted mb-1">Reason</p>
            <pre className="text-xs text-fg whitespace-pre-wrap break-all max-h-48 overflow-auto">
              {d.reason}
            </pre>
          </div>
        )}
      </DetailPanel>

      {manifest && <ManifestPanel manifest={manifest} />}
      <LatestFailedRun manifestId={d.manifestId} />
      <FailedRuns manifestId={d.manifestId} />

      {acking && (
        <NotePrompt
          title={`Acknowledge dead letter #${id}`}
          label="Resolution note (e.g. root cause, why no retry is needed)"
          confirmLabel="Acknowledge"
          busy={busy}
          onConfirm={onAck}
          onClose={() => setAcking(false)}
        />
      )}
    </div>
  );
}

function ManifestPanel({ manifest: m }: { manifest: ManifestDetail }) {
  return (
    <DetailPanel title="Manifest">
      <Fields
        rows={[
          ["Name", <DetailLink to={`/manifests/${m.id}`}>{shortName(m.name)}</DetailLink>],
          ["Enabled", m.isEnabled ? "Yes" : "No"],
          ["Schedule", schedule(m)],
          ["Max retries", m.maxRetries],
          ["Timeout (s)", m.timeoutSeconds ?? "—"],
          ["Priority", m.priority],
          ["Last successful run", formatTime(m.lastSuccessfulRun, "Never")],
        ]}
      />
      {m.properties && m.properties.trim() && (
        <div className="mt-3">
          <p className="text-xs text-muted mb-1">Properties</p>
          <pre className="text-xs text-fg whitespace-pre-wrap break-all max-h-72 overflow-auto font-mono">
            {prettyJson(m.properties)}
          </pre>
        </div>
      )}
    </DetailPanel>
  );
}

// The manifest's newest failed run, read whole (stack trace and input included).
function LatestFailedRun({ manifestId }: { manifestId: number }) {
  const [list] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: 1, manifestId, trainState: "FAILED" },
  });
  const latestId = list.data?.operations?.executions?.items[0]?.id;
  const [detail] = useQuery<{ operations: { executionDetail: ExecutionDetail | null } }>({
    query: EXECUTION_DETAIL,
    variables: { id: latestId ?? 0 },
    pause: latestId == null,
  });
  const run = latestId == null ? null : detail.data?.operations?.executionDetail;
  if (!run) return null;
  return (
    <div className="bg-surface rounded-lg border border-line border-l-4 border-l-danger p-5 mb-6">
      <div className="flex items-center gap-3 mb-4">
        <h2 className="text-sm font-semibold text-danger-fg">Most recent failure</h2>
        <Link to={`/executions/${run.id}`} className="text-xs text-accent-fg hover:underline">
          View run #{run.id}
        </Link>
      </div>
      <Fields
        rows={[
          ["Metadata ID", run.id],
          ["Failure junction", run.failureJunction ?? "—"],
          ["Exception", run.failureException ?? "—"],
          ["Failure class", run.failureClass ? enumLabel(run.failureClass) : "—"],
          ["Reason", run.failureReason ?? "—"],
          ["Started", formatTime(run.startTime)],
          ["Ended", formatTime(run.endTime)],
        ]}
      />
      {run.stackTrace && (
        <div className="mt-3">
          <p className="text-xs text-muted mb-1">Stack trace</p>
          <pre className="text-xs text-danger-fg whitespace-pre-wrap break-all max-h-48 overflow-auto">
            {run.stackTrace}
          </pre>
        </div>
      )}
      {run.input && (
        <div className="mt-3">
          <p className="text-xs text-muted mb-1">Input</p>
          <pre className="text-xs text-fg whitespace-pre-wrap break-all max-h-48 overflow-auto font-mono">
            {prettyJson(run.input)}
          </pre>
        </div>
      )}
    </div>
  );
}

// Every failed run of the manifest, newest first, keyset-paged.
function FailedRuns({ manifestId }: { manifestId: number }) {
  const { afterId, isFirstPage, next, prev } = useKeyset();
  const [{ data, error }] = useQuery<{ operations: { executions: PagedResult<ExecutionSummary> } }>({
    query: EXECUTIONS,
    variables: { take: FAILED_PAGE_SIZE, afterId, manifestId, trainState: "FAILED" },
  });
  const page = data?.operations?.executions;
  const items = page?.items ?? [];
  return (
    <DetailPanel title="Failed execution history">
      {error && <p className="text-sm text-danger-fg">{error.message}</p>}
      {items.length === 0 ? (
        <p className="text-sm text-muted">
          No failed executions found for this manifest.
        </p>
      ) : (
        <>
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-1 font-medium">ID</th>
                <th className="py-1 font-medium">State</th>
                <th className="py-1 font-medium">Started</th>
                <th className="py-1 font-medium">Duration</th>
                <th className="py-1 font-medium">Failure junction</th>
                <th className="py-1 font-medium">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {items.map((e) => (
                <tr key={e.id}>
                  <td className="py-1">
                    <DetailLink to={`/executions/${e.id}`}>#{e.id}</DetailLink>
                  </td>
                  <td className="py-1">
                    <StateBadge state={e.trainState} />
                  </td>
                  <td className="py-1 text-fg-2">{formatTime(e.startTime)}</td>
                  <td className="py-1 text-fg-2">
                    {e.endTime ? formatMs(Date.parse(e.endTime) - Date.parse(e.startTime)) : "—"}
                  </td>
                  <td className="py-1 text-fg-2">{e.failureJunction ?? "—"}</td>
                  <td className="py-1 text-fg-2 max-w-xs truncate" title={e.failureReason ?? ""}>
                    {e.failureReason ?? "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager
            total={page?.totalCount}
            isEstimated={page?.isEstimatedCount}
            isFirstPage={isFirstPage}
            nextCursor={page?.nextCursor}
            onPrev={prev}
            onNext={next}
          />
        </>
      )}
    </DetailPanel>
  );
}

function schedule(m: ManifestDetail): string {
  switch (m.scheduleType) {
    case "CRON":
      return m.cronExpression ? `Cron ${m.cronExpression}` : "Cron";
    case "INTERVAL":
      return m.intervalSeconds ? `Every ${m.intervalSeconds}s` : "Interval";
    default:
      return enumLabel(m.scheduleType);
  }
}
