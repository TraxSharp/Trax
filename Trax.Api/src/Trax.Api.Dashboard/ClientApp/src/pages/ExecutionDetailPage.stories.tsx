import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ExecutionDetailPage } from "./ExecutionDetailPage";
import { MACHINE_IDS, executionDetailScenario } from "../mock/scenarios";
import { getGlobalMockStore } from "../mock/global-store";
import { USER_OWNED_RUN_CANCEL_REFUSAL, userDraftRunsOverlay } from "../mock/store/overlays";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";
import { setAutoConfirm } from "../lib/confirm";

const meta = {
  title: "Pages/Execution detail",
  component: ExecutionDetailPage,
  parameters: {
    routePath: "/runs/:id",
    route: "/runs/903",
    mock: executionDetailScenario,
  },
} satisfies Meta<typeof ExecutionDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

// Opens one of the tabs under the graph.
async function openTab(c: ReturnType<typeof within>, name: string) {
  await userEvent.click(await c.findByRole("tab", { name }));
}

function acceptConfirm() {
  setAutoConfirm(true);
  return () => {
    setAutoConfirm(null);
  };
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true, route: "/runs/1" } };

// An in-progress execution: Cancel requests cancellation.
export const CancelActive: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(c.getByText("Cancellation requested")).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// A run a step of a user's state-machine draft started is read-only to operators: its cancel is
// refused with the API's reason, and nothing is requested.
export const CancelUserDraftRunRefused: Story = {
  parameters: { overlays: [userDraftRunsOverlay([903])] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Cancel" }));
      expect(await c.findByText(USER_OWNED_RUN_CANCEL_REFUSAL)).toBeInTheDocument();
      expect(c.queryByText("Cancellation requested")).not.toBeInTheDocument();
      expect(c.queryByText("Run is no longer cancellable.")).not.toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// After cancellation is requested the Cancel button disappears (no double-cancel) and the
// "Cancellation requested" badge shows; the row is still active so Requeue never appears.
export const CancelHidesCancelButton: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
      expect(c.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
      expect(c.queryByRole("button", { name: "Requeue" })).not.toBeInTheDocument();
      await userEvent.click(c.getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(c.getByText("Cancellation requested")).toBeInTheDocument());
      expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// An active execution offers Cancel but not Requeue.
export const ActiveHasNoRequeue: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
    expect(c.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Requeue" })).not.toBeInTheDocument();
  },
};

// A terminal execution offers Requeue but not Cancel, and shows no cancellation badge.
export const TerminalHasNoCancel: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("button", { name: "Requeue" })).toBeInTheDocument());
    expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(c.queryByText("Cancellation requested")).not.toBeInTheDocument();
  },
};

// A terminal execution: Requeue enqueues a fresh run and toasts.
export const RequeueTerminal: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Requeue" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Requeue" }));
      expect(await c.findByText(/Run requeued/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A run a machine's invoking state queued names that instance, linked to its page, and the queue
// entry it ran from.
export const StartedByMachineInstance: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const instance = await c.findByRole("link", { name: `Machine instance source-partition ${MACHINE_IDS.dispatched}` });
    expect(instance).toHaveAttribute("href", `/machines/source-partition/system/${MACHINE_IDS.dispatched}`);
    expect(c.getByRole("link", { name: "Queue entry 504" })).toHaveAttribute("href", "/runs/queued/504");
  },
};

// An execution with children renders the child tree.
export const ChildTree: Story = {
  parameters: { route: "/runs/800" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Related");
    await waitFor(() => expect(c.getByText("Child runs (3)")).toBeInTheDocument());
    // the child rows come from a separate async query.
    await waitFor(() => expect(c.getByText("ChildOne")).toBeInTheDocument());
  },
};

// A failed run shows its failure class and its train and parent in the header; under Related its
// scheduled time, executor, parent and the run whose decisions it replays (both links), and the
// host's instance id and labels.
export const FailedRunFields: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    expect(c.getByTitle("Failure class")).toHaveTextContent("Transient");
    expect(c.getByRole("link", { name: "Trax.Exec.BetaJob" })).toHaveAttribute("href", "/trains/Trax.Exec.BetaJob");
    expect(c.getByRole("link", { name: "Parent run 800" })).toHaveAttribute("href", "/runs/800");
    await openTab(c, "Related");
    expect(c.getByText("Executor").nextElementSibling).toHaveTextContent("JobRunner");
    expect(c.getByText("Parent run", { selector: "dt" }).nextElementSibling!.querySelector("a")).toHaveAttribute(
      "href",
      "/runs/800",
    );
    expect(c.getByText("Replays decisions of").nextElementSibling!.querySelector("a")).toHaveAttribute(
      "href",
      "/runs/899",
    );
    expect(c.getByText("Scheduled time").nextElementSibling).not.toHaveTextContent("—");
    expect(c.getByText("Instance ID").nextElementSibling).toHaveTextContent("i-mock");
    expect(c.getByText(/"region": "eu-west"/)).toBeInTheDocument();
  },
};

// A run that replays nothing has no "Replays decisions of" row, and no parent.
export const NoReplayLink: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "DeltaJob" })).toBeInTheDocument());
    expect(c.getByText("Started by").nextElementSibling).toHaveTextContent("Started directly");
    await openTab(c, "Related");
    expect(c.queryByText("Replays decisions of")).not.toBeInTheDocument();
    expect(c.getByText("Parent run", { selector: "dt" }).nextElementSibling).toHaveTextContent("—");
  },
};

// The junction timeline of a failed retry: each step with its duration, the yes/no question with
// its answer, confidence and replayed badge, the failed step's class and exception, and the
// attempt badge.
export const JunctionTimelineSteps: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const timeline = await c.findByRole("region", { name: "Junction timeline" });
    const t = within(timeline);
    await waitFor(() => expect(t.getByText("ValidateOrder")).toBeInTheDocument());
    expect(t.getByText("attempt 2")).toBeInTheDocument();
    expect(t.getAllByText("1.5 s").length).toBeGreaterThan(0);
    expect(t.getByText("Yes/No")).toBeInTheDocument();
    expect(t.getByText("is-fraud")).toBeInTheDocument();
    expect(t.getByText("no")).toBeInTheDocument();
    expect(t.getByText("92% confidence")).toBeInTheDocument();
    expect(t.getByText("replayed")).toBeInTheDocument();
    expect(t.getByText("Transient · TimeoutException: the payment gateway did not answer")).toBeInTheDocument();
    expect(timeline.querySelector('[data-position="2"]')).toHaveAttribute("data-state", "FAILED");
  },
};

// A running run: a withheld answer, a withheld step name on another step's track, the step still
// in progress ("so far"), and the step running now in the header.
export const JunctionTimelineWithheldAndRunning: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const timeline = await c.findByRole("region", { name: "Junction timeline" });
    const t = within(timeline);
    await waitFor(() => expect(t.getByText("LoadCart")).toBeInTheDocument());
    expect(t.getByText("Route")).toBeInTheDocument();
    expect(t.getAllByText("withheld").length).toBe(2); // the route's answer + step 3's name
    expect(t.getByText("on track of step #1")).toBeInTheDocument();
    expect(t.getByText(/so far$/)).toBeInTheDocument();
    expect(c.getByText("Running now").nextElementSibling).toHaveTextContent("ProcessStep");
  },
};

// onJunctionEvent: a new step appears live, and a later event for a held step updates it in place.
export const JunctionTimelineLive: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const timeline = await c.findByRole("region", { name: "Junction timeline" });
    await waitFor(() => expect(within(timeline).getByText("ProcessStep")).toBeInTheDocument());
    const store = getGlobalMockStore()!;
    const junction = (position: number, name: string, state: string) => ({
      position,
      kind: "JUNCTION",
      name,
      state,
      startedAt: "2026-07-07T11:55:08.000Z",
      endedAt: state === "IN_PROGRESS" ? null : "2026-07-07T11:55:09.000Z",
      durationMs: state === "IN_PROGRESS" ? null : 1000,
      failureClass: null,
      failureException: null,
      questionKey: null,
      answer: null,
      confidence: null,
      replayed: false,
      decider: null,
      answerWithheld: false,
      attempt: null,
      nameWithheld: false,
      trackPosition: null,
    });
    const publish = (sequence: number, step: ReturnType<typeof junction>, eventType: string) =>
      store.publishEvent("OnJunctionEvent", {
        onJunctionEvent: {
          metadataId: 903,
          eventType,
          timestamp: "2026-07-07T11:55:09.000Z",
          sequence,
          junction: step,
        },
      });
    publish(1, junction(4, "ShipOrder", "IN_PROGRESS"), "JUNCTION_STARTED");
    await waitFor(() => expect(within(timeline).getByText("ShipOrder")).toBeInTheDocument());
    publish(2, junction(3, "ProcessStep", "COMPLETED"), "JUNCTION_COMPLETED");
    await waitFor(() =>
      expect(timeline.querySelector('[data-position="3"]')).toHaveAttribute("data-state", "COMPLETED"),
    );
    // An event for another run is ignored.
    store.publishEvent("OnJunctionEvent", {
      onJunctionEvent: { metadataId: 1, eventType: "JUNCTION_STARTED", timestamp: "", sequence: 3, junction: junction(5, "Elsewhere", "IN_PROGRESS") },
    });
    expect(within(timeline).queryByText("Elsewhere")).not.toBeInTheDocument();
  },
};

// A run with more steps than the API's page says the timeline shows only the first 500.
export const JunctionTimelineCapped: Story = {
  parameters: { route: "/runs/950" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(
      await c.findByText("Showing the first 500 steps; this run recorded more.", undefined, { timeout: 10_000 }),
    ).toBeInTheDocument();
  },
};

// A run with no recorded steps explains why.
export const JunctionTimelineEmpty: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/No steps recorded for this run/)).toBeInTheDocument();
  },
};

const junctionError: MockSchemaOverrides = {
  resolvers: () => {
    const base = executionDetailScenario.resolvers as () => Record<string, Record<string, unknown>>;
    const r = base();
    return {
      ...r,
      OperationsQueries: {
        ...r.OperationsQueries,
        junctionRuns: () => {
          throw new Error("Simulated junction read failure");
        },
      },
    };
  },
};

// A failed junctionRuns read is shown on the timeline; the rest of the page still renders.
export const JunctionTimelineError: Story = {
  parameters: { route: "/runs/902", mock: junctionError },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated junction read failure/)).toBeInTheDocument();
    expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument();
  },
};

// Requeue, ask afresh: the new run asks the model again instead of replaying.
export const RequeueAskAfresh: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Requeue, ask afresh" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Requeue, ask afresh" }));
      expect(await c.findByText(/asks afresh/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Declining the confirmation requeues nothing.
export const RequeueDeclined: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    setAutoConfirm(false);
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Requeue" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Requeue" }));
      expect(c.queryByText(/Run requeued/)).not.toBeInTheDocument();
      expect(c.getByRole("heading", { name: "DeltaJob" })).toBeInTheDocument();
    } finally {
      setAutoConfirm(null);
    }
  },
};

// The run's recorded decisions, in order: a model's answer, a replay that was refused, a withheld
// answer (a sensitive question) and a decision on the track taken on it, which keeps only its time.
export const Decisions: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Decisions");
    const table = await c.findByRole("table", { name: "Decisions" });
    const t = within(table);
    const fraud = within(t.getByTestId("decision-1"));
    expect(fraud.getByText("is-fraud")).toBeInTheDocument();
    expect(fraud.getByText('"no"')).toBeInTheDocument();
    expect(fraud.getByText("FraudDecider")).toBeInTheDocument();
    expect(within(t.getByTestId("decision-2")).getByText("Replay refused")).toHaveAttribute(
      "title",
      "the question changed since the run it replays",
    );
    const carrier = within(t.getByTestId("decision-3"));
    expect(carrier.getByText("Carrier")).toBeInTheDocument();
    expect(carrier.getByText("Answer withheld")).toBeInTheDocument();
    expect(carrier.getByText("Withheld")).toBeInTheDocument();
    const onTrack = within(t.getByTestId("decision-4"));
    expect(onTrack.getByText("Track withheld")).toBeInTheDocument();
    expect(onTrack.queryByRole("button", { name: "Details" })).not.toBeInTheDocument();
    expect(c.getByTestId("decisions-withheld-note")).toBeInTheDocument();
  },
};

// Details opens a decision's question, shadow answers and fingerprint; a withheld one says why its
// answer is missing.
export const DecisionDetails: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Decisions");
    const table = await c.findByRole("table", { name: "Decisions" });
    await userEvent.click(within(within(table).getByTestId("decision-1")).getByRole("button", { name: "Details" }));
    expect(c.getByText(/"Is this order fraudulent\?"/)).toBeInTheDocument();
    expect(c.getByText("Shadow answers")).toBeInTheDocument();
    expect(c.getByText("fp-fraud")).toBeInTheDocument();
    await userEvent.click(within(within(table).getByTestId("decision-3")).getByRole("button", { name: "Details" }));
    expect(c.getByText(/The answer, any refusal, shadow answers and routes are withheld/)).toBeInTheDocument();
    expect(c.queryByText("fp-fraud")).not.toBeInTheDocument(); // one open at a time
  },
};

// A replayed answer the run refused is flagged both ways.
export const RefusedReplayedDecision: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Decisions");
    const row = within(await c.findByTestId("decision-5"));
    expect(row.getByText("Refused")).toBeInTheDocument();
    expect(row.getAllByText("Replayed").length).toBeGreaterThan(0);
  },
};

// More decisions than a page: Next reads the rest after the last one shown, Previous goes back.
export const DecisionsPaged: Story = {
  parameters: { route: "/runs/950" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Decisions");
    await c.findByText("question-1");
    expect(c.queryByText("question-26")).not.toBeInTheDocument();
    const panel = c.getByRole("table", { name: "Decisions" }).parentElement as HTMLElement;
    await userEvent.click(within(panel).getByRole("button", { name: "Next" }));
    expect(await c.findByText("question-26")).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Next" })).toBeDisabled();
    await userEvent.click(within(panel).getByRole("button", { name: "Previous" }));
    expect(await c.findByText("question-1")).toBeInTheDocument();
  },
};

export const NoDecisions: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Decisions");
    expect(await c.findByText("This run recorded no decisions.")).toBeInTheDocument();
  },
};

// A run queued to replay an earlier run that asked afresh instead is marked so.
export const ReplayAbandoned: Story = {
  parameters: { route: "/runs/951" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("Replay abandoned")).toBeInTheDocument();
    expect(await c.findByRole("link", { name: "Retries dead letter 31" })).toHaveAttribute("href", "/runs/dead-letters/31");
    expect(c.getByRole("link", { name: "Queue entry 505" })).toHaveAttribute("href", "/runs/queued/505");
    await openTab(c, "Related");
    expect(c.getByText("Abandoned: it could not be honoured, so the run asked afresh")).toBeInTheDocument();
    expect(c.getByRole("link", { name: "902" })).toHaveAttribute("href", "/runs/902");
  },
};

// The run's own log reads oldest first, with the text filters; no run filter or column.
export const RunLogOldestFirst: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await openTab(c, "Logs");
    const table = await c.findByRole("table", { name: "Log entries" });
    await waitFor(() => expect(within(table).getAllByRole("row").length).toBe(5));
    expect(within(within(table).getAllByRole("row")[1]).getByText("Charging card for order 42")).toBeInTheDocument();
    expect(c.queryByLabelText("Run")).not.toBeInTheDocument();
    await userEvent.type(c.getByLabelText("Message contains"), "timed out");
    await waitFor(() => expect(within(table).getAllByRole("row").length).toBe(2));
  },
};

// ── Run graph, checkpoints and resume ────────────────────────────────────

const graphOf = async (c: ReturnType<typeof within>) => c.findByRole("region", { name: "Run graph" });
// A node as the shared Graph draws it: React Flow's node, by the step's id.
const nodeOf = (graph: HTMLElement, id: string) =>
  graph.querySelector(`.react-flow__node[data-id="${id}"]`) as HTMLElement | null;
const selectNode = async (graph: HTMLElement, id: string) => {
  await waitFor(() => expect(nodeOf(graph, id)).not.toBeNull());
  await userEvent.click(within(nodeOf(graph, id)!).getByRole("button"));
};
const panelOf = (graph: HTMLElement) => graph.querySelector("aside[data-node-id]") as HTMLElement | null;

// A failed run after a checkpoint: one graph, opened at the step that failed with its error in the
// panel; the checkpoint is marked (never what it holds), the routing step's tracks branch from it,
// and the steps it can resume at offer "Resume from here" in their panel.
export const RunGraphCheckpoints: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(panelOf(graph)).toHaveAttribute("data-node-id", "Summarize#2"));
    const panel = within(panelOf(graph)!);
    expect(panel.getByText("Transient · TimeoutException")).toBeInTheDocument();
    expect(panel.getByRole("button", { name: "Resume from here" })).toBeInTheDocument();
    expect(within(nodeOf(graph, "Findings#1")!).getByLabelText(/checkpoint/)).toBeInTheDocument();
    expect(within(nodeOf(graph, "Summarize#2")!).getByRole("button")).toHaveAttribute("aria-pressed", "true");
    expect(within(graph).getByText("Short")).toBeInTheDocument();
    expect(within(graph).getByText("Long")).toBeInTheDocument();
    await selectNode(graph, "Fetch#0");
    expect(panelOf(graph)).toHaveAttribute("data-node-id", "Fetch#0");
    expect(within(panelOf(graph)!).queryByRole("button", { name: "Resume from here" })).not.toBeInTheDocument();
    expect(c.getByRole("button", { name: "Resume" })).toBeInTheDocument();
    expect(c.queryByRole("region", { name: "Junction progress" })).not.toBeInTheDocument();
  },
};

// Selecting a node highlights its bar in the timeline, and pressing a bar selects its node.
export const TimelineFollowsTheGraph: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    const timeline = await c.findByRole("region", { name: "Junction timeline" });
    await waitFor(() => expect(timeline.querySelector('[data-position="2"]')).toHaveAttribute("data-selected", "true"));
    await userEvent.click(within(timeline).getByRole("button", { name: "Step #0 ValidateOrder" }));
    await waitFor(() => expect(panelOf(graph)).toHaveAttribute("data-node-id", "Fetch#0"));
    expect(timeline.querySelector('[data-position="0"]')).toHaveAttribute("data-selected", "true");
    expect(timeline.querySelector('[data-position="2"]')).not.toHaveAttribute("data-selected");
  },
};

// Resume queues a run that skips past the latest checkpoint, beside Requeue.
export const ResumeAfterCheckpoint: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await userEvent.click(await c.findByRole("button", { name: "Resume" }));
      expect(await c.findByText("Run queued to resume.")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// "Resume from here" resumes at that node.
export const ResumeFromNode: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      const graph = await graphOf(c);
      await selectNode(graph, "Publish#4");
      await userEvent.click(within(panelOf(graph)!).getByRole("button", { name: "Resume from here" }));
      expect(await c.findByText("Run queued to resume.")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A host that refuses the resume: its reason is shown and the page stays.
export const ResumeRefused: Story = {
  parameters: {
    route: "/runs/902",
    overlays: [
      {
        mutations: {
          ResumeExecution: () => ({
            operations: {
              resumeExecution: {
                success: false,
                message: "Summarize needs Findings; no checkpoint before it holds one. Nothing was queued.",
                id: null,
              },
            },
          }),
        },
      },
    ],
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await userEvent.click(await c.findByRole("button", { name: "Resume" }));
      expect(await c.findByText(/no checkpoint before it holds one/)).toBeInTheDocument();
      expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// The run that resumed: the steps before its resume point are restored, a Parallel step's branches
// leave it side by side, and a completed run offers no resume.
export const ResumedRunRestored: Story = {
  parameters: { route: "/runs/952" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(nodeOf(graph, "Fetch#0")).not.toBeNull());
    expect(within(nodeOf(graph, "Fetch#0")!).getByRole("button")).toHaveTextContent("Restored");
    expect(within(graph).getByText("Citations")).toBeInTheDocument();
    expect(within(graph).getByText("Recency")).toBeInTheDocument();
    expect(within(graph).getByText("LegacyStep")).toBeInTheDocument();
    expect(panelOf(graph)).toBeNull();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    await selectNode(graph, "Summarize#2");
    expect(within(panelOf(graph)!).queryByRole("button", { name: "Resume from here" })).not.toBeInTheDocument();
    expect(within(graph).queryByRole("note")).not.toBeInTheDocument();
  },
};

// A run whose chain has changed since it started says so above its graph.
export const ChainChangedSinceTheRun: Story = {
  parameters: { route: "/runs/958" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    expect(await within(graph).findByRole("note")).toHaveTextContent("chain has changed since this run started");
  },
};

// A resumed run names the run it resumed, linked, and the node it resumed at.
export const ResumedRunNamesItsSource: Story = {
  parameters: { route: "/runs/952" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    await openTab(c, "Related");
    const resumes = c.getByText("Resumes", { selector: "dt" }).nextElementSibling as HTMLElement;
    expect(within(resumes).getByRole("link", { name: "902" })).toHaveAttribute("href", "/runs/902");
    expect(resumes).toHaveTextContent("902 at Summarize#2");
  },
};

// A run that resumed after the latest checkpoint names no node.
export const ResumedAfterLatestCheckpoint: Story = {
  parameters: {
    route: "/runs/952",
    mock: {
      resolvers: () => {
        const r = (executionDetailScenario.resolvers as () => Record<string, Record<string, (...a: unknown[]) => unknown>>)();
        return {
          ...r,
          OperationsQueries: {
            ...r.OperationsQueries,
            executionDetail: (...args: unknown[]) => ({
              ...(r.OperationsQueries.executionDetail(...args) as object),
              resumeAt: null,
            }),
          },
        };
      },
    } satisfies MockSchemaOverrides,
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    await openTab(c, "Related");
    const resumes = c.getByText("Resumes", { selector: "dt" }).nextElementSibling as HTMLElement;
    expect(resumes).toHaveTextContent("902 after its latest checkpoint");
  },
};

// A run that was not resumed shows no Resumes field.
export const NotResumedHasNoResumesField: Story = {
  parameters: { route: "/runs/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    await openTab(c, "Related");
    expect(c.queryByText("Resumes", { selector: "dt" })).not.toBeInTheDocument();
  },
};

// A run with no saved input cannot be resumed, as it cannot be requeued: the checkpoint still
// shows, but nothing offers a resume.
export const NoInputNoResume: Story = {
  parameters: { route: "/runs/954" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(panelOf(graph)).toHaveAttribute("data-node-id", "Summarize#2"));
    expect(within(nodeOf(graph, "Findings#1")!).getByLabelText(/checkpoint/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    expect(within(graph).queryByRole("button", { name: "Resume from here" })).not.toBeInTheDocument();
  },
};

// A train this host has no declared graph for says so.
export const NoDeclaredGraph: Story = {
  parameters: { route: "/runs/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    expect(await within(graph).findByText(/no declared graph for this train/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
  },
};

// A run whose routing nests deeper than a query can follow nested tracks: every node is drawn, at
// any depth, and the page opens at the failed step six tracks deep, which offers "Resume from
// here". A Parallel step's branches both leave it, and a step the host stopped mid-junction shows
// as interrupted.
export const DeepRunGraph: Story = {
  parameters: { route: "/runs/956" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    const lanes = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0";
    const leaf = `${lanes}/Left/Check#0/Open/Pick#0/Deep/Leaf#0`;
    await waitFor(() => expect(panelOf(graph)).toHaveAttribute("data-node-id", leaf));
    const panel = within(panelOf(graph)!);
    expect(panel.getByText("Deep", { selector: "dd" })).toBeInTheDocument();
    expect(panel.getByRole("button", { name: "Resume from here" })).toBeInTheDocument();
    for (const id of [lanes, `${lanes}/Left/Check#0`, `${lanes}/Right/Tally#0`, "Plan#1/Slow/Wait#0"])
      expect(nodeOf(graph, id)).not.toBeNull();
    expect(within(nodeOf(graph, `${lanes}/Right/Tally#0`)!).getByRole("button")).toHaveTextContent("Interrupted");
    expect(within(nodeOf(graph, "Plan#1/Slow/Wait#0")!).getByRole("button")).toHaveTextContent("Skipped");
    expect(within(graph).getByText("Left")).toBeInTheDocument();
    expect(within(graph).getByText("Right")).toBeInTheDocument();
  },
};

// Resuming at the deep step names it.
export const ResumeFromDeepNode: Story = {
  parameters: { route: "/runs/956" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    const leaf = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0/Deep/Leaf#0";
    try {
      const graph = await graphOf(c);
      await waitFor(() => expect(panelOf(graph)).toHaveAttribute("data-node-id", leaf));
      await userEvent.click(within(panelOf(graph)!).getByRole("button", { name: "Resume from here" }));
      expect(await c.findByText("Run queued to resume.")).toBeInTheDocument();
      expect(getGlobalMockStore()!.getState().executionResumes).toHaveProperty("956");
    } finally {
      restore();
    }
  },
};

const runGraphError: MockSchemaOverrides = {
  resolvers: () => {
    const base = executionDetailScenario.resolvers as () => Record<string, Record<string, unknown>>;
    const r = base();
    return {
      ...r,
      OperationsQueries: {
        ...r.OperationsQueries,
        runGraph: () => {
          throw new Error("Simulated run graph read failure");
        },
      },
    };
  },
};

// A run graph that could not be read says so in its section, rather than looking like a run that
// cannot be resumed; the rest of the page still shows.
export const RunGraphReadFailed: Story = {
  parameters: { route: "/runs/902", mock: runGraphError },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    expect(await within(graph).findByText(/Simulated run graph read failure/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument();
  },
};
