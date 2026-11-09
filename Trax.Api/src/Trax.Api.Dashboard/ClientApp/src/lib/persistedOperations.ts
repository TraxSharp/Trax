import { useTraxQuery } from "./consoleClient";
import { persistedOperationsAvailableDocument } from "../trax/index.g";

// Whether this caller may manage persisted operations on the host: it serves the persistedOperations
// root (UsePersistedOperations with GateManagement) and the caller meets that gate. It reads
// operations.config.canManagePersistedOperations, which every host serves and answers false where
// management is not served, so no host is sent a field it does not have. A caller the operations
// gate refuses sees no pages anyway; its refusal is not a broken refresh, so the query opts out of
// the header's "last refresh failed" mark. The console client sends it once for the sidebar and the pages.
export function usePersistedOperationsAvailable(): { available: boolean; checking: boolean } {
  const [{ data, fetching, error }] = useTraxQuery(persistedOperationsAvailableDocument, { ignoreRefreshStatus: true });
  const available = !error && data?.operations?.config.canManagePersistedOperations === true;
  return { available, checking: fetching && !data && !error };
}

/** The route for one operation; the default tenant has no `tenant` parameter. */
export function persistedOperationPath(id: string, tenantKey: string | null | undefined): string {
  const base = `/api-admin/persisted-operations/${encodeURIComponent(id)}`;
  return tenantKey ? `${base}?tenant=${encodeURIComponent(tenantKey)}` : base;
}

/** "(default)" for the default tenant. */
export function tenantLabel(tenantKey: string | null | undefined): string {
  return tenantKey ? tenantKey : "(default)";
}
