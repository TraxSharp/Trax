import { describe, expect, it } from "vitest";
import { COLUMNS } from "./cases";
import { treeOf, type Attempt, type FlatGraphNode, type Journal, type RunGraph, type Step } from "./types";
import { columnsOf, lanesOf, modelCallsOf, pendingQuestion, questionOf } from "./workflowView";
// Run graphs and steps read from the Recovery host: a refund crashing while paying and its retry, one crashing while
// emailing and the retry that started from its checkpoint, and a topic map.
import fixtures from "./workflowView.fixtures.json";

type Recorded = { graph: { hasGraph: boolean; allNodes: FlatGraphNode[] }; steps: Step[] };

const graphOf = (recorded: Recorded): RunGraph => ({ hasGraph: recorded.graph.hasGraph, nodes: treeOf(recorded.graph.allNodes) });

const T0 = Date.parse("2026-10-13T12:00:00Z");

function attempt(id: number, recorded: Recorded | null, overrides: Partial<Attempt> = {}): Attempt {
  return {
    id: String(id),
    origin: "manifest",
    startedBy: null,
    trainState: "COMPLETED",
    startTime: new Date(T0).toISOString(),
    endTime: new Date(T0 + 5000).toISOString(),
    failureJunction: null,
    failureReason: null,
    steps: Object.fromEntries((recorded?.steps ?? []).map((s) => [s.position, s])),
    journal: null,
    graph: recorded ? graphOf(recorded) : null,
    ...overrides,
  };
}

/** A graph like `recorded`'s, with each node in the state `stateOf` gives it. */
function withStates(recorded: Recorded, stateOf: (node: FlatGraphNode) => FlatGraphNode["state"]): RunGraph {
  return graphOf({ ...recorded, graph: { ...recorded.graph, allNodes: recorded.graph.allNodes.map((n) => ({ ...n, state: stateOf(n) })) } });
}

describe("columnsOf", () => {
  it("lays the refund's chain out as the page's fallback does", () => {
    expect(columnsOf(graphOf(fixtures.refundRetry as Recorded).nodes)).toEqual(COLUMNS.REFUND);
  });

  it("lays the topic map's branches side by side, as the page's fallback does", () => {
    const graph = graphOf({ steps: [], graph: { hasGraph: true, allNodes: fixtures.topicMapNodes as FlatGraphNode[] } });
    expect(columnsOf(graph.nodes)).toEqual(COLUMNS.TOPIC_MAP);
  });

  it("names the question a node asks", () => {
    expect(questionOf("Decide<ChoiceDecision<Problem>>#0")).toBe("Problem");
    expect(questionOf("Parallel#0/cocitation/Gate<SameTopic>#0")).toBe("SameTopic");
  });
});

describe("lanesOf", () => {
  it("draws one empty lane before anything runs", () => {
    const lanes = lanesOf([], COLUMNS.REFUND, T0);
    expect(lanes).toHaveLength(1);
    expect(lanes[0].slots.map((s) => s.tone)).toEqual(COLUMNS.REFUND.map(() => "idle"));
    expect(lanes[0].status).toBeNull();
  });

  it("fills a crash while paying, then the retry with both answers replayed", () => {
    const first = attempt(239, fixtures.refundCrashedWhilePaying as Recorded, { trainState: "FAILED" });
    const retry = attempt(260, fixtures.refundRetry as Recorded);
    const replayed = Object.fromEntries(Object.values(retry.steps).map((s) => [s.position, { ...s, replayed: s.kind !== "JUNCTION" && s.kind !== "ROUTE" }]));
    const [crashed, recovered] = lanesOf([first, { ...retry, steps: replayed }], COLUMNS.REFUND, T0 + 20000);

    expect(crashed.label).toBe("attempt 1");
    expect(crashed.status).toEqual({ text: "Crashed", tone: "failed" });
    expect(crashed.slots.map((s) => [s.label, s.tone])).toEqual([
      ["LoadRefundCase", "done"],
      ["Problem?", "asked"],
      ["RequestPhotos", "done"],
      ["ApproveRefund?", "asked"],
      ["IssuePayment", "failed"],
      ["Checkpoint", "idle"],
      ["NotifyCustomer", "idle"],
    ]);
    expect(crashed.slots[1].note).toBe("Damaged");
    expect(crashed.slots[3].note).toBe("Yes");
    expect(crashed.slots[6].dimmed).toBe(true);

    expect(recovered.status).toEqual({ text: "Recovered", tone: "done" });
    expect(recovered.slots.map((s) => s.tone)).toEqual(["done", "replayed", "done", "replayed", "done", "saved", "done"]);
    expect(recovered.slots[1].note).toBe("Damaged, replayed");
  });

  it("shows a crash while emailing saving the checkpoint, and the retry restoring it", () => {
    const first = attempt(920, fixtures.refundCrashedWhileEmailing as Recorded, { trainState: "FAILED" });
    const retry = attempt(943, fixtures.refundResumedRetry as Recorded);
    const [crashed, resumed] = lanesOf([first, retry], COLUMNS.REFUND, T0 + 20000);

    expect(crashed.slots[5]).toMatchObject({ label: "Checkpoint", tone: "saved", note: "saved" });
    expect(crashed.slots[6].tone).toBe("failed");
    expect(resumed.slots.map((s) => [s.label, s.tone])).toEqual([
      ["LoadRefundCase", "restored"],
      ["Problem?", "restored"],
      ["RequestPhotos", "restored"],
      ["ApproveRefund?", "restored"],
      ["IssuePayment", "restored"],
      ["Checkpoint", "restored"],
      ["NotifyCustomer", "done"],
    ]);
    expect(resumed.status?.text).toBe("Recovered");
  });

  it("knows a retry started from the checkpoint before its graph is read", () => {
    const retry = attempt(943, fixtures.refundResumedRetry as Recorded, { graph: null, trainState: "IN_PROGRESS" });
    const [, lane] = lanesOf([attempt(920, fixtures.refundCrashedWhileEmailing as Recorded, { trainState: "FAILED" }), retry], COLUMNS.REFUND, T0);
    expect(lane.slots[5].tone).toBe("restored");
    expect(lane.slots[0].tone).toBe("restored");
  });

  it("fills a running junction part way and marks the question being asked", () => {
    const recorded = fixtures.refundCrashedWhilePaying as Recorded;
    const loaded = recorded.steps.filter((s) => s.position === 0);
    const graph = withStates(recorded, (n) => (n.id === "LoadRefundCase#0" ? "COMPLETED" : n.kind === "RESOLVE" || n.kind === "CHECKPOINT" ? "NOT_RECORDED" : "NOT_REACHED"));
    const running = attempt(239, null, { trainState: "IN_PROGRESS", graph, steps: Object.fromEntries(loaded.map((s) => [s.position, s])) });
    const [lane] = lanesOf([running], COLUMNS.REFUND, Date.parse(loaded[0].endedAt!) + 400);
    expect(lane.slots[1]).toMatchObject({ tone: "running", note: "asking" });
    expect(lane.slots[1].fill).toBeGreaterThan(0);
    expect(lane.slots[1].fill).toBeLessThan(1);
    expect(lane.status).toEqual({ text: "Running", tone: "running" });
  });
});

describe("pendingQuestion", () => {
  it("is the next Decide once the step before it ended", () => {
    const recorded = fixtures.refundCrashedWhilePaying as Recorded;
    const graph = withStates(recorded, (n) => (n.id === "LoadRefundCase#0" ? "COMPLETED" : "NOT_REACHED"));
    expect(pendingQuestion(graph, recorded.steps.slice(0, 1))).toBe("Decide<ChoiceDecision<Problem>>#0");
  });

  it("is nothing while a junction runs, or once the question is answered", () => {
    const recorded = fixtures.refundCrashedWhilePaying as Recorded;
    const running = withStates(recorded, (n) => (n.id === "LoadRefundCase#0" ? "IN_PROGRESS" : "NOT_REACHED"));
    expect(pendingQuestion(running, [])).toBeNull();
    const asked = withStates(recorded, (n) => (n.id === "LoadRefundCase#0" ? "COMPLETED" : "NOT_REACHED"));
    expect(pendingQuestion(asked, recorded.steps.slice(0, 2))).toBeNull();
  });

  it("finds a question waiting inside a Parallel branch", () => {
    const nodes = (fixtures.topicMapNodes as FlatGraphNode[]).map((n) => ({
      ...n,
      state: (n.id === "LoadCorpus#0" || n.id.endsWith("CountSharedReferences#0") ? "COMPLETED" : n.id === "Parallel#0" || n.id.includes("embedding") ? "IN_PROGRESS" : "NOT_REACHED") as FlatGraphNode["state"],
    }));
    expect(pendingQuestion({ hasGraph: true, nodes: treeOf(nodes) }, [])).toBe("Parallel#0/cocitation/Decide<YesNoDecision<SameTopic>>#0");
  });
});

describe("modelCallsOf", () => {
  const journal = (replayed: boolean): Journal => ({
    replayDecisionsOf: replayed ? 239 : null,
    replayAbandoned: false,
    decisions: [
      {
        questionKey: "Problem",
        occurrence: 0,
        replayed,
        replayRefused: null,
        model: "demo-rules-1.0",
        decider: replayed ? null : "DemoDecider",
        stateHash: "k1:26b2e1d2a6e2",
        question:
          '{"key": "Problem", "type": "choice", "options": [{"name": "Damaged", "description": "It arrived damaged or broken."}, {"name": "Lost", "description": "It never arrived."}], "instructions": "What went wrong with this order?"}',
        answer: '{"type": "choice", "model": "demo-rules-1.0", "choice": "Damaged", "confidence": 0.91, "probabilities": null}',
      },
      {
        questionKey: "ApproveRefund",
        occurrence: 0,
        replayed,
        replayRefused: null,
        model: "demo-rules-1.0",
        decider: replayed ? null : "DemoDecider",
        stateHash: "k1:c62beaae7adf",
        question: '{"key": "ApproveRefund", "type": "yes_no", "yes": "Pay it now.", "no": "Decline it.", "instructions": "Should this refund be paid?"}',
        answer: '{"type": "yes_no", "model": "demo-rules-1.0", "probability": 0.93}',
      },
    ],
  });

  it("puts each question's words and answer on a card, and badges a replay", () => {
    const first = attempt(239, fixtures.refundCrashedWhilePaying as Recorded, { trainState: "FAILED", journal: journal(false) });
    const retry = attempt(260, fixtures.refundRetry as Recorded, { journal: journal(true) });
    retry.steps = Object.fromEntries(Object.values(retry.steps).map((s) => [s.position, { ...s, replayed: s.kind === "CHOICE" || s.kind === "YES_NO" }]));
    const calls = modelCallsOf([first, retry]);

    expect(calls.map((c) => [c.attempt, c.questionKey, c.answer, c.badge?.text])).toEqual([
      ["attempt 1", "Problem", "Damaged, 91% sure", "model asked"],
      ["attempt 1", "ApproveRefund", "93% yes: the Yes track", "model asked"],
      ["attempt 2", "Problem", "Damaged, 91% sure", "replayed"],
      ["attempt 2", "ApproveRefund", "93% yes: the Yes track", "replayed"],
    ]);
    expect(calls[0].question?.instructions).toBe("What went wrong with this order?");
    expect(calls[0].question?.options?.map((o) => o.name)).toEqual(["Damaged", "Lost"]);
    expect(calls[0].model).toBe("demo-rules-1.0");
    expect(calls[0].ms).toBeGreaterThan(0);
  });

  it("adds a waiting card, with the words an earlier attempt was asked", () => {
    const recorded = fixtures.refundCrashedWhilePaying as Recorded;
    const first = attempt(239, recorded, { trainState: "FAILED", journal: journal(false) });
    const graph = withStates(recorded, (n) => (n.id === "LoadRefundCase#0" ? "COMPLETED" : "NOT_REACHED"));
    const asking = attempt(261, null, {
      trainState: "IN_PROGRESS",
      origin: "requeue",
      graph,
      steps: Object.fromEntries(recorded.steps.slice(0, 1).map((s) => [s.position, s])),
    });
    const calls = modelCallsOf([first, asking]);
    expect(calls[calls.length - 1]).toMatchObject({ state: "waiting", questionKey: "Problem", attempt: "requeue" });
    expect(calls[calls.length - 1].question?.instructions).toBe("What went wrong with this order?");
    expect(modelCallsOf([first, asking], (a) => a.id === "261").some((c) => c.state === "waiting")).toBe(false);
  });

  it("badges an answer asked again because the state changed", () => {
    const changed = journal(false);
    changed.decisions[0] = { ...changed.decisions[0], replayRefused: "state-changed" };
    const first = attempt(239, fixtures.refundCrashedWhilePaying as Recorded, { trainState: "FAILED" });
    const retry = attempt(260, fixtures.refundRetry as Recorded, { journal: changed });
    retry.steps = Object.fromEntries(Object.values(retry.steps).map((s) => [s.position, { ...s, replayed: false }]));
    expect(modelCallsOf([first, retry])[2].badge).toEqual({ text: "asked afresh: state changed", tone: "afresh" });
  });
});
