import type { GraphEdgeSpec, GraphNodeSpec, GraphTone } from "./graphLayout";

export interface BoardScenario {
  file: string;
  line: number;
  scenario: string;
}

export interface BoardSticky {
  id: string;
  kind: string;
  machine: string;
  label: string;
  scenarios: BoardScenario[];
}

export interface Board {
  machine: string;
  lanes: string[];
  stickies: BoardSticky[];
  links: { from: string; to: string }[];
}

// Each flow role in the closest tone the console has. The role is also written on the sticky, so colour never
// carries it alone.
const TONE: Record<string, GraphTone> = {
  actor: "idle",
  command: "info",
  event: "warn",
  policy: "accent",
  aggregate: "idle",
  view: "ok",
};

/** The board on the shared Graph: a node per sticky, its role and lane under its label, an edge per arrow. */
export function boardGraph(board: Board): { nodes: GraphNodeSpec[]; edges: GraphEdgeSpec[] } {
  const severalLanes = board.lanes.length > 1;
  return {
    nodes: board.stickies.map((s) => ({
      id: s.id,
      label: s.label,
      detail: severalLanes ? `${s.kind} · ${s.machine}` : s.kind,
      tone: TONE[s.kind] ?? "idle",
    })),
    edges: board.links.map((l) => ({ id: `${l.from}->${l.to}`, source: l.from, target: l.to })),
  };
}

/** Whether the host file the board was drawn from had provenance: some sticky names a scenario. */
export function hasProvenance(board: Board): boolean {
  return board.stickies.some((s) => s.scenarios.length > 0);
}
