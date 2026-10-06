import { useEffect, useReducer } from "react";
import { getGlobalMockStore } from "../../mock/global-store";

// Dev-only viewer for the GraphQL mock's session overlay. Reads the process-global store
// (globalThis.__mockStore) that the mock client exposes, and re-renders on every write and
// published event. Only meaningful when the app runs in mock mode (?mock).
export function MockOverlayPage() {
  const store = getGlobalMockStore();
  const [, force] = useReducer((n: number) => n + 1, 0);

  useEffect(() => {
    if (!store) return;
    const unsubState = store.subscribe(() => force());
    const unsubEvents = store.subscribeEvents(() => force());
    return () => {
      unsubState();
      unsubEvents();
    };
  }, [store]);

  if (!store) {
    return (
      <div>
        <Header />
        <p className="text-sm text-muted">
          No mock store is active. Run the app in mock mode (append{" "}
          <code className="font-mono">?mock</code> to the URL) to populate the overlay.
        </p>
      </div>
    );
  }

  const state = store.getState();
  const history = [...store.getHistory()].reverse();
  const events = [...store.getEvents()].reverse();
  const stateEntries = Object.entries(state);

  return (
    <div>
      <Header onReset={() => store.reset()} />

      <Panel title={`Overlay delta (${stateEntries.length})`}>
        {stateEntries.length === 0 ? (
          <Empty>No writes yet. Mutate something (e.g. cancel a work-queue entry).</Empty>
        ) : (
          <pre className="text-xs font-mono text-fg overflow-auto max-h-72 whitespace-pre-wrap">
            {JSON.stringify(state, null, 2)}
          </pre>
        )}
      </Panel>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
        <Panel title={`Change history (${history.length})`}>
          {history.length === 0 ? (
            <Empty>No changes recorded.</Empty>
          ) : (
            <ul className="space-y-2 text-sm">
              {history.map((c, i) => (
                <li key={`${c.at}-${i}`} className="border-b border-line pb-2">
                  <div className="flex items-center justify-between">
                    <span className="font-medium text-fg">{c.action}</span>
                    <span className="text-xs text-muted">{new Date(c.at).toLocaleTimeString()}</span>
                  </div>
                  {c.changed.map((f) => (
                    <div key={f.key} className="text-xs text-muted font-mono break-all">
                      {f.key}: {preview(f.before)} → {preview(f.after)}
                    </div>
                  ))}
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel title={`Published events (${events.length})`}>
          {events.length === 0 ? (
            <Empty>No subscription events published.</Empty>
          ) : (
            <ul className="space-y-1.5 text-sm">
              {events.map((e, i) => (
                <li key={`${e.at}-${i}`} className="flex items-center justify-between gap-3">
                  <span className="font-mono text-fg-2 truncate" title={JSON.stringify(e.data)}>
                    {e.operationName}
                  </span>
                  <span className="text-xs text-muted whitespace-nowrap">
                    {new Date(e.at).toLocaleTimeString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </div>
  );
}

function Header({ onReset }: { onReset?: () => void }) {
  return (
    <div className="flex items-center justify-between mb-6">
      <div>
        <h1 className="text-2xl font-bold text-fg">Mock overlay</h1>
        <p className="text-sm text-muted mt-1">
          Session delta written by mocked mutations, plus the published event log.
        </p>
      </div>
      {onReset && (
        <button
          onClick={onReset}
          className="text-sm px-3 py-1.5 rounded-lg border border-line-strong text-fg-2 hover:bg-hover"
        >
          Reset overlay
        </button>
      )}
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-3">{title}</h2>
      {children}
    </div>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="text-sm text-muted">{children}</p>;
}

function preview(value: unknown): string {
  const json = value === undefined ? "undefined" : JSON.stringify(value);
  return json && json.length > 80 ? `${json.slice(0, 77)}…` : String(json);
}
