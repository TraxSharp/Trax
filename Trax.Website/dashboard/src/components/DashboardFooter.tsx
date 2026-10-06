import { useMemo } from "react";
import { useQuery } from "urql";
import { SERVER_VERSION } from "../graphql/queries";

/**
 * The footer, as the Blazor DashboardFooter: the version of Trax the API host runs
 * (config.version). It says which Trax release serves the API, not the host application's own
 * version. Nothing is shown until the version is known.
 */
export function DashboardFooter() {
  // A footer read must not raise the header's "last refresh failed" mark.
  const context = useMemo(() => ({ ignoreRefreshStatus: true }), []);
  const [{ data }] = useQuery<{ operations: { config: { version: string } } | null }>({
    query: SERVER_VERSION,
    context,
  });
  const version = data?.operations?.config?.version;
  return (
    <footer className="py-4 text-center text-xs text-muted" data-testid="dashboard-footer">
      {version ? `Trax Dashboard — v${version}` : "\u00a0"}
    </footer>
  );
}
