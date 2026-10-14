import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { PERSISTED_OPERATIONS } from "../graphql/queries";
import { PersistedOperationEditor } from "../components/PersistedOperationEditor";
import { formatTime } from "../lib/format";
import {
  persistedOperationPath,
  tenantLabel,
  usePersistedOperationsAvailable,
} from "../lib/persistedOperations";
import { toast } from "../lib/toast";
import type { PersistedOperation } from "../types";

const PAGE_SIZE = 25;

type TenantScope = "all" | "default" | "named";
type StatusScope = "all" | "active" | "inactive";

interface ListData {
  operations: {
    persistedOperations: {
      persistedOperations: { items: PersistedOperation[]; totalCount: number };
    };
  };
}

// Server-managed GraphQL operations resolved by stable id (operations.persistedOperations), as the
// Blazor PersistedOperationsPage lists them: filter by tenant, status and id prefix, open one, or
// upload a new one. The namespace exists only when the host calls UsePersistedOperations; without
// it the page says how to enable it. The API pages this list by offset (it has no keyset cursor).
export function PersistedOperationsPage() {
  const { available, checking } = usePersistedOperationsAvailable();

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-1">
        Persisted operations
      </h1>
      <p className="text-sm text-muted mb-6">
        Server-managed GraphQL operations resolved by stable id.
      </p>
      {checking ? (
        <p className="text-muted">Loading…</p>
      ) : available ? (
        <PersistedOperationsList />
      ) : (
        <NotEnabled />
      )}
    </div>
  );
}

export function NotEnabled() {
  return (
    <div className="bg-info-soft border border-info-line text-info-fg rounded-lg p-4 text-sm">
      Persisted Operations is not enabled on this server. Call{" "}
      <code className="font-mono">UsePersistedOperations(...)</code> on the Trax GraphQL builder to
      enable management.
    </div>
  );
}

function PersistedOperationsList() {
  const [tenantScope, setTenantScope] = useState<TenantScope>("all");
  const [tenantKey, setTenantKey] = useState("");
  const [status, setStatus] = useState<StatusScope>("all");
  const [idPrefix, setIdPrefix] = useState("");
  const [skip, setSkip] = useState(0);
  const [uploading, setUploading] = useState(false);

  // The API reads a null tenant key as every tenant and an empty one as the default tenant.
  const filter = {
    isActive: status === "all" ? null : status === "active",
    tenantKey:
      tenantScope === "default"
        ? ""
        : tenantScope === "named" && tenantKey.trim()
          ? tenantKey.trim()
          : null,
    idStartsWith: idPrefix.trim() ? idPrefix.trim() : null,
  };
  const [result, reexecute] = useQuery<ListData>({
    query: PERSISTED_OPERATIONS,
    variables: { filter, skip, take: PAGE_SIZE },
  });
  const page = result.data?.operations?.persistedOperations?.persistedOperations;
  const rows = page?.items ?? [];
  const total = page?.totalCount ?? 0;

  const onFilter = (apply: () => void) => {
    apply();
    setSkip(0);
  };

  const select =
    "text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

  return (
    <>
      <div className="flex flex-wrap items-end gap-3 mb-4">
        <label className="text-xs text-muted">
          Tenant
          <select
            aria-label="Tenant"
            value={tenantScope}
            onChange={(e) =>
              onFilter(() => {
                const scope = e.target.value as TenantScope;
                setTenantScope(scope);
                if (scope !== "named") setTenantKey("");
              })
            }
            className={`${select} block mt-1`}
          >
            <option value="all">All tenants</option>
            <option value="default">Default tenant</option>
            <option value="named">Named tenant</option>
          </select>
        </label>
        {tenantScope === "named" && (
          <label className="text-xs text-muted">
            Tenant key
            <input
              aria-label="Tenant key"
              value={tenantKey}
              onChange={(e) => onFilter(() => setTenantKey(e.target.value))}
              className={`${select} block mt-1`}
            />
          </label>
        )}
        <label className="text-xs text-muted">
          Status
          <select
            aria-label="Status"
            value={status}
            onChange={(e) => onFilter(() => setStatus(e.target.value as StatusScope))}
            className={`${select} block mt-1`}
          >
            <option value="all">All</option>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </label>
        <label className="text-xs text-muted">
          Id starts with
          <input
            aria-label="Id starts with"
            value={idPrefix}
            onChange={(e) => onFilter(() => setIdPrefix(e.target.value))}
            className={`${select} block mt-1`}
          />
        </label>
        <button
          onClick={() => setUploading(true)}
          className="ml-auto text-sm px-3 py-1.5 rounded-md bg-accent text-on-accent hover:bg-accent-hover"
        >
          Upload
        </button>
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
              <th className="px-4 py-2 font-medium">Id</th>
              <th className="px-4 py-2 font-medium">Tenant</th>
              <th className="px-4 py-2 font-medium">Operation</th>
              <th className="px-4 py-2 font-medium">Version</th>
              <th className="px-4 py-2 font-medium">Active</th>
              <th className="px-4 py-2 font-medium">Updated</th>
              <th className="px-4 py-2 font-medium">Description</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((op) => (
              <tr key={`${op.tenantKey ?? ""}|${op.id}`}>
                <td className="px-4 py-2">
                  <Link
                    to={persistedOperationPath(op.id, op.tenantKey)}
                    className="font-medium font-mono text-accent-fg hover:underline"
                  >
                    {op.id}
                  </Link>
                </td>
                <td className="px-4 py-2 text-fg-2">{tenantLabel(op.tenantKey)}</td>
                <td className="px-4 py-2 text-fg-2">{op.operationName}</td>
                <td className="px-4 py-2 text-fg-2">{op.version}</td>
                <td className="px-4 py-2">
                  <span
                    className={`text-xs px-2 py-0.5 rounded-full ${
                      op.isActive
                        ? "bg-ok-soft text-ok-fg"
                        : "bg-raised text-muted"
                    }`}
                  >
                    {op.isActive ? "Active" : "Inactive"}
                  </span>
                </td>
                <td className="px-4 py-2 text-fg-2">{formatTime(op.updatedAt)}</td>
                <td className="px-4 py-2 text-fg-2 max-w-xs truncate" title={op.description ?? ""}>
                  {op.description ?? "—"}
                </td>
              </tr>
            ))}
            {page && rows.length === 0 && (
              <tr>
                <td colSpan={7} className="px-4 py-8 text-center text-muted">
                  No persisted operations.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between mt-4 text-sm">
        <span className="text-muted">
          {page && total > 0
            ? `${skip + 1}–${Math.min(skip + rows.length, total)} of ${total.toLocaleString()}`
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

      {uploading && (
        <PersistedOperationEditor
          onClose={() => setUploading(false)}
          onSaved={() => {
            toast("Operation uploaded.", "success");
            reexecute({ requestPolicy: "network-only" });
          }}
        />
      )}
    </>
  );
}
