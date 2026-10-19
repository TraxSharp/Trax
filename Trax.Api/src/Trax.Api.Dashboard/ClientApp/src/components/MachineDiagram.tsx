import { useMemo } from "react";
import { Link } from "react-router-dom";
import { Graph, type GraphLegendEntry } from "./Graph";
import { machineGraph, type StateCount } from "../lib/machineGraph";
import { shortName } from "../lib/format";
import { machinePath } from "../lib/machineInstances";
import type { MachineDefinition } from "../types";

const DOCS_DECLARATIVE = "https://traxsharp.net/docs/statemachine/delegates-and-declarative-rules";

/**
 * A machine's states and transitions on the shared Graph, with the trains and machines its states
 * start listed under it as links (a node is a button, so it cannot hold one). With `counts`, each
 * state is badged with its instances and stuck ones; with `current`, the instance's state is lit.
 */
export function MachineDiagram({
  machine,
  counts,
  current,
  selected,
  onSelect,
}: {
  machine: MachineDefinition;
  counts?: ReadonlyMap<string, StateCount>;
  current?: string;
  selected?: string | null;
  onSelect?: (state: string) => void;
}) {
  const { nodes, edges } = useMemo(() => machineGraph(machine, { counts, current }), [machine, counts, current]);

  if (machine.graphUnavailableReason != null)
    return (
      <div className="rounded-md border border-warn-line bg-warn-soft p-4 text-sm text-warn-fg">
        <p>
          {machine.name} has no diagram: some of its rules are written as code the host cannot describe, so the
          console cannot draw them.{" "}
          <a href={DOCS_DECLARATIVE} target="_blank" rel="noreferrer" className="underline">
            Declared rules and code rules
          </a>
        </p>
        <details className="mt-2">
          <summary className="cursor-pointer">What the host says</summary>
          <p className="mt-1 text-xs">{machine.graphUnavailableReason}</p>
        </details>
      </div>
    );

  const legend: GraphLegendEntry[] =
    current != null
      ? [{ label: "Where it is now", tone: "accent" }]
      : [
          { label: "Has instances", tone: "info" },
          { label: "Has stuck instances", tone: "danger" },
          { label: "No instances", tone: "idle" },
        ];
  const starts = machine.states.filter((s) => s.invokesTrain || s.spawnsMachine);

  return (
    <div>
      <Graph
        nodes={nodes}
        edges={edges}
        ariaLabel={`States of ${machine.name}`}
        selectedId={selected ?? undefined}
        onSelect={onSelect}
        legend={legend}
        emptyText={`${machine.name} declares no states.`}
      />
      <p className="mt-1 text-xs text-muted">
        A solid edge is a command; a dashed one is a timer; an edge marked "run done", "run failed" or "run
        cancelled" is taken when the run its state started ends that way.
      </p>
      {starts.length > 0 && (
        <ul aria-label="What its states start" className="mt-2 space-y-0.5 text-sm text-fg-2">
          {starts.map((s) => (
            <li key={s.name}>
              <span className="font-medium text-fg">{s.name}</span>{" "}
              {s.invokesTrain ? (
                <>
                  runs the train{" "}
                  <Link
                    to={`/trains/${encodeURIComponent(s.invokesTrain)}`}
                    title={s.invokesTrain}
                    className="text-accent-fg hover:underline"
                  >
                    {shortName(s.invokesTrain)}
                  </Link>
                </>
              ) : (
                <>
                  starts the machine{" "}
                  <Link to={machinePath(s.spawnsMachine!)} className="text-accent-fg hover:underline">
                    {s.spawnsMachine}
                  </Link>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
