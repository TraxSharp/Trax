// The "Who can talk to whom" map: each edge between the app and what is around it, coloured by the checks the
// recorded Hub + Workers deploy on Kubernetes ran (src/data/deploy-recordings.json, "who can reach what"): the
// NetworkPolicy probes from a pod of the app and from a pod that is not, and the egress check run inside the hub's
// pod as the app. An edge is open or refused only when every check behind it says so.
import type { Check, DeployRecordings, DeployScenario, EgressCheck } from "./deploy-replay.ts";

export type NodeKey = "app" | "database" | "rabbitmq" | "other" | "internet" | "metadata";

export interface ReachEdge {
  key: string;
  from: NodeKey;
  to: NodeKey;
  result: "open" | "refused" | "mixed";
  /** The recorded checks behind it, as the recording words them. */
  sources: string[];
  /** When the first of them was checked, in the recording. */
  at: number;
}

const policy = (who: RegExp, what: string) => (c: Check) => c.group === "policy" && who.test(c.who) && c.what === what;
const egress = (kind: string, targets: string[]) => (e: EgressCheck) => e.kind === kind && targets.includes(e.target);
const APP = /^a pod of the app$/;
const OTHER = /^a pod in the namespace that is not the app's$/;

const EDGES: { key: string; from: NodeKey; to: NodeKey; checks: ((c: Check) => boolean)[]; egress: ((e: EgressCheck) => boolean)[] }[] = [
  { key: "app-database", from: "app", to: "database", checks: [policy(APP, "database:5432")], egress: [egress("tcp", ["database:5432"])] },
  { key: "app-rabbitmq", from: "app", to: "rabbitmq", checks: [policy(APP, "rabbitmq:5672")], egress: [egress("tcp", ["rabbitmq:5672"])] },
  { key: "other-database", from: "other", to: "database", checks: [policy(OTHER, "database:5432")], egress: [] },
  { key: "other-rabbitmq", from: "other", to: "rabbitmq", checks: [policy(OTHER, "rabbitmq:5672")], egress: [] },
  { key: "app-internet", from: "app", to: "internet", checks: [], egress: [egress("dns", ["example.com"]), egress("tcp", ["[2606:4700:4700::1111]:443"])] },
  { key: "app-metadata", from: "app", to: "metadata", checks: [], egress: [egress("tcp", ["169.254.169.254:80", "169.254.170.23:80"])] },
];

const opened = (got: string) => got === "open";

/** The map's edges, read off one recorded "who can reach what" run. */
export function reachEdges(s: DeployScenario): ReachEdge[] {
  return EDGES.map((edge) => {
    const checks = s.checks.filter((c) => edge.checks.some((m) => m(c)));
    const rows = s.egress.filter((e) => edge.egress.some((m) => m(e)));
    const results = [...checks.map((c) => opened(c.result)), ...rows.map((e) => opened(e.got))];
    const result = results.length === 0 ? "mixed" : results.every(Boolean) ? "open" : results.every((r) => !r) ? "refused" : "mixed";
    return {
      key: edge.key,
      from: edge.from,
      to: edge.to,
      result,
      sources: [...checks.map((c) => `${c.who} to ${c.what}: ${c.result}`), ...rows.map((e) => `from inside the hub, ${e.kind} ${e.target}: ${e.got}`)],
      at: Math.min(...checks.map((c) => c.t), ...rows.map((e) => e.t)),
    };
  });
}

/** The recorded run the map is read from: Hub + Workers on Kubernetes. */
export function reachScenario(rec: DeployRecordings): DeployScenario | null {
  return rec.shapes["hub-workers"]?.targets.kubernetes?.scenarios["who-can-reach-what"] ?? null;
}
