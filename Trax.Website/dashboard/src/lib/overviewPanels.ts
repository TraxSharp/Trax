import { useSyncExternalStore } from "react";

// Which Overview panels the user shows, persisted in localStorage like the other user settings.
// The same five toggles as the Blazor dashboard's "Dashboard Components" settings; every panel is
// shown by default.
export type OverviewPanel =
  | "serverHealth"
  | "summaryCards"
  | "executionsChart"
  | "failures"
  | "avgDuration";

export const OVERVIEW_PANELS: { key: OverviewPanel; label: string; description: string }[] = [
  { key: "serverHealth", label: "Server Health", description: "Process CPU, memory, GC heap, and uptime." },
  {
    key: "summaryCards",
    label: "Summary Cards",
    description: "Executions Today, Success Rate, Currently Running, Dead Letters.",
  },
  {
    key: "executionsChart",
    label: "Executions Chart",
    description:
      "Completed, failed, and cancelled executions over time (last hour or last 24 hours), and throughput by train.",
  },
  { key: "failures", label: "Failures", description: "Top failing trains over the last 7 days." },
  {
    key: "avgDuration",
    label: "Avg Execution Duration",
    description: "Average train execution times over the last 7 days.",
  },
];

export type OverviewPanels = Record<OverviewPanel, boolean>;

const KEY = "trax:overview-panels";
export const DEFAULT_OVERVIEW_PANELS: OverviewPanels = {
  serverHealth: true,
  summaryCards: true,
  executionsChart: true,
  failures: true,
  avgDuration: true,
};

const listeners = new Set<() => void>();
let current = read();

function read(): OverviewPanels {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    if (!raw) return DEFAULT_OVERVIEW_PANELS;
    const parsed = JSON.parse(raw) as Partial<OverviewPanels>;
    const out = { ...DEFAULT_OVERVIEW_PANELS };
    for (const k of Object.keys(out) as OverviewPanel[])
      if (typeof parsed[k] === "boolean") out[k] = parsed[k];
    return out;
  } catch {
    return DEFAULT_OVERVIEW_PANELS;
  }
}

function write(next: OverviewPanels) {
  current = next;
  try {
    globalThis.localStorage?.setItem(KEY, JSON.stringify(next));
  } catch {
    /* ignore */
  }
  listeners.forEach((fn) => fn());
}

export function getOverviewPanels(): OverviewPanels {
  return current;
}

export function setOverviewPanel(panel: OverviewPanel, visible: boolean): void {
  write({ ...current, [panel]: visible });
}

/** Show every panel again (the Blazor "Reset Default"). */
export function resetOverviewPanels(): void {
  write(DEFAULT_OVERVIEW_PANELS);
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

/** The current panel visibility (reactive). */
export function useOverviewPanels(): OverviewPanels {
  return useSyncExternalStore(subscribe, getOverviewPanels, getOverviewPanels);
}
