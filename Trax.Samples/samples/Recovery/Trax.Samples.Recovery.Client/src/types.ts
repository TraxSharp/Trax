export type Scenario = "RESEARCH" | "REFUND" | "TOPIC_MAP";

export type StepKind = "JUNCTION" | "CHOICE" | "SCORE" | "YES_NO" | "ROUTE";

export interface Step {
  position: number;
  kind: StepKind;
  name: string;
  state: "IN_PROGRESS" | "COMPLETED" | "FAILED" | "CANCELLED";
  startedAt: string;
  endedAt: string | null;
  durationMs: number | null;
  failureClass: string | null;
  failureException: string | null;
  questionKey: string | null;
  answer: string | null;
  confidence: number | null;
  replayed: boolean;
  answerWithheld: boolean;
  nameWithheld: boolean;
  trackPosition: number | null;
  attempt: number | null;
  /** The declared node the step ran for, as operations.runGraph names it; in a branch, Parallel#0/<branch>/... */
  nodeId: string | null;
}

export type NodeState =
  | "NOT_REACHED"
  | "IN_PROGRESS"
  | "COMPLETED"
  | "FAILED"
  | "CANCELLED"
  | "SKIPPED"
  | "NOT_RECORDED"
  | "WITHHELD"
  /** In a resumed run, a step before the point it resumed at: skipped, its result restored from the checkpoint. */
  | "RESTORED"
  /** Started and never recorded an end, in a run that has ended: the host stopped mid-step, or its end event was lost. */
  | "INTERRUPTED";

/** One declared node of a run graph, with where the run stands there. */
export interface GraphNode {
  id: string;
  kind: string;
  junction: string | null;
  state: NodeState;
  replayed: boolean;
  trackTaken: string | null;
  /** Whether resumeExecution can resume the run at this node. */
  canResume: boolean;
  /** Whether a checkpoint the run can resume from is stored at this node (never what it holds). */
  checkpointed: boolean;
  tracks: GraphTrack[];
}

/** A track of a routing node, or a branch of a Parallel node. */
export interface GraphTrack {
  name: string;
  taken: boolean;
  nodes: GraphNode[];
}

/** operations.runGraph: the train's declared chain with the run's steps laid on it. */
export interface RunGraph {
  hasGraph: boolean;
  nodes: GraphNode[];
}

/**
 * One entry of operations.runGraph's allNodes: a node without its tracks' nodes, and where it sits. The page reads
 * the flat list because a query cannot follow nested tracks past the server's field-cycle limit.
 */
export interface FlatGraphNode extends Omit<GraphNode, "tracks"> {
  /** The routing or Parallel step whose track the node sits on; null at the chain's top level. */
  parentId: string | null;
  /** The name of that track or branch; null at the chain's top level. */
  track: string | null;
  tracks: Omit<GraphTrack, "nodes">[];
}

/** Rebuilds the tree from allNodes, whose order is depth first with each track's nodes in declared order. */
export const treeOf = (flat: FlatGraphNode[]): GraphNode[] => {
  const under = new Map<string, FlatGraphNode[]>();
  const keyOf = (parentId: string | null, track: string | null) => `${parentId ?? ""}\u0000${track ?? ""}`;
  for (const node of flat) {
    const key = keyOf(node.parentId, node.track);
    under.set(key, [...(under.get(key) ?? []), node]);
  }
  const build = (parentId: string | null, track: string | null): GraphNode[] =>
    (under.get(keyOf(parentId, track)) ?? []).map(({ parentId: _parent, track: _track, tracks, ...node }) => ({
      ...node,
      tracks: tracks.map((t) => ({ ...t, nodes: build(node.id, t.name) })),
    }));
  return build(null, null);
};

export interface JournalEntry {
  questionKey: string;
  occurrence: number;
  replayed: boolean;
  replayRefused: string | null;
  model: string | null;
  stateHash: string | null;
}

export interface Journal {
  replayDecisionsOf: number | null;
  replayAbandoned: boolean;
  decisions: JournalEntry[];
}

export interface Attempt {
  id: number;
  /** "manifest" for the manifest's runs, "requeue" for a requeueExecution, "resume" for a resumeExecution. */
  origin: "manifest" | "requeue" | "resume";
  /** What started it, when it was not the first run or the manifest's own retry. */
  startedBy: "askAfresh" | "requeue" | "resume" | null;
  trainState: string;
  startTime: string;
  endTime: string | null;
  failureJunction: string | null;
  failureReason: string | null;
  steps: Record<number, Step>;
  journal: Journal | null;
  graph: RunGraph | null;
}

export interface RunInfo {
  runId: string;
  manifestId: number;
  manifestExternalId: string;
  trainName: string;
  armedCrash: string;
  maxRetries: number;
  scenario: Scenario;
}

export type Tone = "info" | "model" | "replay" | "error" | "success" | "system";

export interface ConsoleLine {
  key: string;
  /** Milliseconds since Run was pressed. */
  t: number;
  tone: Tone;
  text: string;
}

export type Phase = "idle" | "starting" | "running" | "backoff" | "retrying" | "done" | "requeue" | "dead";

/** The node the page resumes a research run at: the step after its checkpoint, as operations.runGraph names it. */
export const RESUME_AT = "Summarize#0";

/** How many nodes a resumed run skipped and restored from its checkpoint, at any depth of its graph. */
export const restoredIn = (graph: RunGraph | null | undefined): number => {
  const count = (nodes: GraphNode[] | undefined): number =>
    (nodes ?? []).reduce(
      (n, node) => n + (node.state === "RESTORED" ? 1 : 0) + node.tracks.reduce((t, track) => t + count(track.nodes), 0),
      0,
    );
  return count(graph?.nodes);
};

/** What the reader did during the backoff, if anything. */
export type Fork = "none" | "askAfresh" | "changeData";

/** An answer as the page shows it: a probability or score to two places, a choice as it is. */
export const shownAnswer = (answer: string | null) =>
  answer != null && /^-?\d+\.\d{3,}$/.test(answer) ? Number(answer).toFixed(2) : answer;

/** The branch a step ran in, from its node id (Parallel#0/cocitation/...), or null outside a branch. */
export const branchOf = (nodeId: string | null | undefined) => nodeId?.match(/^Parallel#\d+\/([^/]+)\//)?.[1] ?? null;
