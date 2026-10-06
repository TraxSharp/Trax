import { LogsGrid } from "../components/LogsGrid";

// Every run's log entries, newest first, with the filters the API serves. The grid is the one the
// run page uses for its own log.
export function LogsPage() {
  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-2">Logs</h1>
      <p className="text-sm text-muted mb-6">
        Log entries captured during train execution, including messages, exceptions, and diagnostic information.
      </p>
      <LogsGrid poll />
    </div>
  );
}
