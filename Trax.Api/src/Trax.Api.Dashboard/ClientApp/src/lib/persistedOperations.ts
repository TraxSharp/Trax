import { useMemo } from "react";
import { useQuery } from "urql";
import { PERSISTED_OPERATIONS_AVAILABLE } from "../graphql/queries";

// Whether the host exposes operations.persistedOperations (it calls UsePersistedOperations). The
// probe selects only the namespace's __typename, which reads nothing; a host without the namespace
// fails it at validation. Its failure is the answer, not a broken refresh, so it opts out of the
// header's "last refresh failed" mark. urql dedupes it across the sidebar and the pages.
export function usePersistedOperationsAvailable(): { available: boolean; checking: boolean } {
  const context = useMemo(() => ({ ignoreRefreshStatus: true }), []);
  const [{ data, fetching, error }] = useQuery<{
    operations: { persistedOperations: { __typename: string } | null } | null;
  }>({ query: PERSISTED_OPERATIONS_AVAILABLE, context });
  const available = !error && Boolean(data?.operations?.persistedOperations);
  return { available, checking: fetching && !data && !error };
}

/** The route for one operation; the default tenant has no `tenant` parameter. */
export function persistedOperationPath(id: string, tenantKey: string | null | undefined): string {
  const base = `/persisted-operations/${encodeURIComponent(id)}`;
  return tenantKey ? `${base}?tenant=${encodeURIComponent(tenantKey)}` : base;
}

/** "(default)" for the default tenant. */
export function tenantLabel(tenantKey: string | null | undefined): string {
  return tenantKey ? tenantKey : "(default)";
}
