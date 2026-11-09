import { useMemo, useState } from "react";
import { Graph, type GraphLegendEntry } from "./Graph";
import { boardGraph, hasProvenance, type Board } from "../lib/machineBoard";

const LEGEND: GraphLegendEntry[] = [
  { label: "Command", tone: "info" },
  { label: "Event", tone: "warn" },
  { label: "Policy", tone: "accent" },
  { label: "View", tone: "ok" },
  { label: "Actor or machine", tone: "idle" },
];

/**
 * The event-storming board of a machine served from features, on the shared Graph: who acts, what they ask, what
 * happens, the rules between and the views, across each machine it joins. Clicking a sticky lists the scenarios that
 * put it there, by file, line and name; a host file with no provenance draws the board without them.
 */
export function MachineBoard({ board }: { board: Board }) {
  const [selected, setSelected] = useState<string | null>(null);
  const { nodes, edges } = useMemo(() => boardGraph(board), [board]);
  const sticky = board.stickies.find((s) => s.id === selected);
  const provenance = hasProvenance(board);

  return (
    <div>
      <Graph
        nodes={nodes}
        edges={edges}
        ariaLabel={`Board of ${board.machine}`}
        selectedId={selected ?? undefined}
        onSelect={(id) => setSelected(id === selected ? null : id)}
        legend={LEGEND}
        emptyText={`${board.machine} has nothing on its board.`}
      />
      {!provenance && (
        <p className="mt-2 text-xs text-muted">
          This host file records no scenarios, so the board does not say which ones produced each sticky.
        </p>
      )}
      {sticky && provenance && (
        <div aria-label="Scenarios behind it" className="mt-3 text-sm">
          <p className="font-medium text-fg">
            {sticky.label} <span className="text-muted">({sticky.kind})</span>
          </p>
          {sticky.scenarios.length === 0 ? (
            <p className="text-muted">No scenario produced this sticky on its own.</p>
          ) : (
            <ul className="mt-1 space-y-0.5 text-fg-2">
              {sticky.scenarios.map((s) => (
                <li key={`${s.file}:${s.line}`}>
                  <span className="font-mono text-xs">
                    {s.file}:{s.line}
                  </span>{" "}
                  {s.scenario}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
