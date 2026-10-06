import { useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQuery } from "urql";
import { PERSISTED_OPERATION_DETAIL } from "../graphql/queries";
import {
  DEACTIVATE_PERSISTED_OPERATION,
  RESTORE_PERSISTED_OPERATION,
} from "../graphql/mutations";
import { BackLink, DetailPanel, Fields, Loading } from "../components/detail";
import {
  DeactivationReasonDialog,
  PersistedOperationEditor,
} from "../components/PersistedOperationEditor";
import { formatTime } from "../lib/format";
import { tenantLabel, usePersistedOperationsAvailable } from "../lib/persistedOperations";
import { toast } from "../lib/toast";
import { NotEnabled } from "./PersistedOperationsPage";
import type {
  PersistedOperation,
  PersistedOperationHistoryEntry,
  PersistedOperationPayload,
} from "../types";

const HISTORY_TAKE = 100;

interface DetailData {
  operations: {
    persistedOperations: {
      persistedOperation: PersistedOperation | null;
      persistedOperationHistory: PersistedOperationHistoryEntry[];
    };
  };
}

// One persisted operation (the tenant comes from ?tenant=; none is the default tenant): its
// fields, its document and its audit history, with Edit, Deactivate (a reason is required) and
// Restore. Mirrors the Blazor PersistedOperationDetailPage.
export function PersistedOperationDetailPage() {
  const id = useParams().id ?? "";
  const [params] = useSearchParams();
  const tenantKey = params.get("tenant") || null;
  const { available, checking } = usePersistedOperationsAvailable();

  return (
    <div>
      <BackLink to="/persisted-operations" label="Persisted operations" />
      <div className="flex items-center gap-3 mt-2 mb-6">
        <h1 className="text-2xl font-bold font-mono text-fg">{id}</h1>
        <span title="Tenant" className="text-xs px-2 py-0.5 rounded-full bg-raised text-fg-2">
          {tenantLabel(tenantKey)}
        </span>
      </div>
      {checking ? <Loading /> : available ? <Detail id={id} tenantKey={tenantKey} /> : <NotEnabled />}
    </div>
  );
}

function Detail({ id, tenantKey }: { id: string; tenantKey: string | null }) {
  const [result, reexecute] = useQuery<DetailData>({
    query: PERSISTED_OPERATION_DETAIL,
    variables: { id, tenantKey, historyTake: HISTORY_TAKE },
  });
  const [, deactivate] = useMutation(DEACTIVATE_PERSISTED_OPERATION);
  const [, restore] = useMutation(RESTORE_PERSISTED_OPERATION);
  const [editing, setEditing] = useState(false);
  const [deactivating, setDeactivating] = useState(false);
  const [busy, setBusy] = useState(false);

  const ns = result.data?.operations?.persistedOperations;
  const op = ns?.persistedOperation;
  const history = ns?.persistedOperationHistory ?? [];
  const refetch = () => reexecute({ requestPolicy: "network-only" });

  if (result.error && !op)
    return <p className="text-danger-fg">{result.error.message}</p>;
  if (result.fetching && !ns) return <Loading />;
  if (!op)
    return (
      <p className="text-warn-fg">
        Persisted operation &apos;{id}&apos; not found for tenant {tenantLabel(tenantKey)}.
      </p>
    );

  function report(summary: string, payload: PersistedOperationPayload | undefined, error?: string) {
    if (error) toast(error, "error");
    else if (payload?.success) toast(`${summary}: ${id} (${tenantLabel(tenantKey)})`, "success");
    else toast(`Not changed. ${(payload?.errors ?? []).map((e) => e.message).join(" ")}`, "error");
    refetch();
  }

  async function onDeactivate(reason: string) {
    setBusy(true);
    const r = await deactivate({ input: { id, reason, tenantKey } });
    setBusy(false);
    setDeactivating(false);
    report("Deactivated", r.data?.operations?.persistedOperations?.deactivatePersistedOperation, r.error?.message);
  }

  async function onRestore() {
    setBusy(true);
    const r = await restore({ input: { id, tenantKey } });
    setBusy(false);
    report("Restored", r.data?.operations?.persistedOperations?.restorePersistedOperation, r.error?.message);
  }

  return (
    <>
      <div className="flex gap-2 mb-4">
        <button
          onClick={() => setEditing(true)}
          disabled={busy}
          className="text-sm px-3 py-1 rounded-md border border-line-strong text-fg-2 hover:bg-hover disabled:opacity-50"
        >
          Edit
        </button>
        {op.isActive ? (
          <button
            onClick={() => setDeactivating(true)}
            disabled={busy}
            className="text-sm px-3 py-1 rounded-md border border-warn-line text-warn-fg hover:bg-warn-soft disabled:opacity-50"
          >
            Deactivate
          </button>
        ) : (
          <button
            onClick={onRestore}
            disabled={busy}
            className="text-sm px-3 py-1 rounded-md border border-ok-line text-ok-fg hover:bg-ok-soft disabled:opacity-50"
          >
            Restore
          </button>
        )}
      </div>

      <DetailPanel>
        <Fields
          rows={[
            ["Tenant", tenantLabel(op.tenantKey)],
            ["Operation", `${op.operationName} (v${op.version})`],
            ["Active", op.isActive ? "Yes" : "No"],
            ...(!op.isActive
              ? ([["Deprecation reason", op.deprecationReason ?? "—"]] as [string, React.ReactNode][])
              : []),
            ["Shape fingerprint", <code className="text-xs">{op.shapeFingerprint}</code>],
            ["Description", op.description ?? "—"],
            ["Created", formatTime(op.createdAt)],
            ["Updated", formatTime(op.updatedAt)],
          ]}
        />
      </DetailPanel>

      <DetailPanel title="Document">
        <pre className="text-xs font-mono whitespace-pre-wrap break-all text-fg">
          {op.document}
        </pre>
      </DetailPanel>

      <DetailPanel title="History">
        {history.length === 0 ? (
          <p className="text-sm text-muted">No history recorded.</p>
        ) : (
          <table className="w-full text-sm" aria-label="History">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-1 font-medium">When</th>
                <th className="py-1 font-medium">Change</th>
                <th className="py-1 font-medium">Shape</th>
                <th className="py-1 font-medium">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {history.map((h) => (
                <tr key={h.historyId}>
                  <td className="py-1 text-fg-2">{formatTime(h.changedAt)}</td>
                  <td className="py-1 text-fg">{h.changeType}</td>
                  <td className="py-1">
                    <code className="text-xs" title={h.shapeFingerprint}>
                      {h.shapeFingerprint.slice(0, 12)}…
                    </code>
                  </td>
                  <td className="py-1 text-fg-2">{h.changedReason ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </DetailPanel>

      {editing && (
        <PersistedOperationEditor
          existing={{
            id: op.id,
            tenantKey: op.tenantKey,
            document: op.document,
            description: op.description,
            version: op.version,
          }}
          onClose={() => setEditing(false)}
          onSaved={() => {
            toast("Operation updated.", "success");
            refetch();
          }}
        />
      )}
      {deactivating && (
        <DeactivationReasonDialog
          busy={busy}
          onConfirm={onDeactivate}
          onClose={() => setDeactivating(false)}
        />
      )}
    </>
  );
}
