import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ExecutionDetailPage } from "./ExecutionDetailPage";
import { executionDetailScenario } from "../mock/scenarios";
import { getGlobalMockStore } from "../mock/global-store";
import { USER_OWNED_RUN_CANCEL_REFUSAL, userDraftRunsOverlay } from "../mock/store/overlays";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

const meta = {
  title: "Pages/Execution detail",
  component: ExecutionDetailPage,
  parameters: {
    routePath: "/executions/:id",
    route: "/executions/903",
    mock: executionDetailScenario,
  },
} satisfies Meta<typeof ExecutionDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true, route: "/executions/1" } };

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
      expect(c.queryByText("Execution is no longer cancellable.")).not.toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// After cancellation is requested the Cancel button disappears (no double-cancel) and the
// "Cancellation requested" badge shows; the row is still active so Re-queue never appears.
export const CancelHidesCancelButton: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
      expect(c.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
      expect(c.queryByRole("button", { name: "Re-queue" })).not.toBeInTheDocument();
      await userEvent.click(c.getByRole("button", { name: "Cancel" }));
      await waitFor(() => expect(c.getByText("Cancellation requested")).toBeInTheDocument());
      expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// An active execution offers Cancel but not Re-queue.
export const ActiveHasNoRequeue: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "AlphaJob" })).toBeInTheDocument());
    expect(c.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Re-queue" })).not.toBeInTheDocument();
  },
};

// A terminal execution offers Re-queue but not Cancel, and shows no cancellation badge.
export const TerminalHasNoCancel: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("button", { name: "Re-queue" })).toBeInTheDocument());
    expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(c.queryByText("Cancellation requested")).not.toBeInTheDocument();
  },
};

// A terminal execution: Re-queue enqueues a fresh run and toasts.
export const RequeueTerminal: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Re-queue" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Re-queue" }));
      expect(await c.findByText(/Execution re-queued/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// An execution with children renders the child tree.
export const ChildTree: Story = {
  parameters: { route: "/executions/800" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Child executions (3)")).toBeInTheDocument());
    // the child rows come from a separate async query.
    await waitFor(() => expect(c.getByText("ChildOne")).toBeInTheDocument());
  },
};

// A failed run shows its failure class, scheduled time, executor, parent and the run whose
// decisions it replays (both links), and the host's instance id and labels.
export const FailedRunFields: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    expect(c.getByTitle("Failure class")).toHaveTextContent("Transient");
    expect(c.getByText("Executor").nextElementSibling).toHaveTextContent("JobRunner");
    expect(c.getByText("Parent ID").nextElementSibling!.querySelector("a")).toHaveAttribute("href", "/executions/800");
    expect(c.getByText("Replays decisions of").nextElementSibling!.querySelector("a")).toHaveAttribute(
      "href",
      "/executions/899",
    );
    expect(c.getByText("Scheduled time").nextElementSibling).not.toHaveTextContent("—");
    expect(c.getByText("Instance ID").nextElementSibling).toHaveTextContent("i-mock");
    expect(c.getByText(/"region": "eu-west"/)).toBeInTheDocument();
  },
};

// A run that replays nothing has no "Replays decisions of" row, and no parent.
export const NoReplayLink: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "DeltaJob" })).toBeInTheDocument());
    expect(c.queryByText("Replays decisions of")).not.toBeInTheDocument();
    expect(c.getByText("Parent ID").nextElementSibling).toHaveTextContent("—");
  },
};

// The junction timeline of a failed retry: each step with its duration, the yes/no question with
// its answer, confidence and replayed badge, the failed step's class and exception, and the
// attempt badge.
export const JunctionTimelineSteps: Story = {
  parameters: { route: "/executions/902" },
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
// in progress ("so far"), and the junction progress panel.
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
    expect(c.getByText("Junction progress")).toBeInTheDocument();
    expect(c.getByText("Currently running").nextElementSibling).toHaveTextContent("ProcessStep");
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
  parameters: { route: "/executions/950" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(
      await c.findByText("Showing the first 500 steps; this run recorded more.", undefined, { timeout: 10_000 }),
    ).toBeInTheDocument();
  },
};

// A run with no recorded steps explains why.
export const JunctionTimelineEmpty: Story = {
  parameters: { route: "/executions/900" },
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
  parameters: { route: "/executions/902", mock: junctionError },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated junction read failure/)).toBeInTheDocument();
    expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument();
  },
};

// Re-queue, ask afresh: the new run asks the model again instead of replaying.
export const RequeueAskAfresh: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Re-queue, ask afresh" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Re-queue, ask afresh" }));
      expect(await c.findByText(/asks afresh/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Declining the confirmation re-queues nothing.
export const RequeueDeclined: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const original = window.confirm;
    window.confirm = () => false;
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Re-queue" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Re-queue" }));
      expect(c.queryByText(/Execution re-queued/)).not.toBeInTheDocument();
      expect(c.getByRole("heading", { name: "DeltaJob" })).toBeInTheDocument();
    } finally {
      window.confirm = original;
    }
  },
};

// The run's recorded decisions, in order: a model's answer, a replay that was refused, a withheld
// answer (a sensitive question) and a decision on the track taken on it, which keeps only its time.
export const Decisions: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
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
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
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
    const row = within(await c.findByTestId("decision-5"));
    expect(row.getByText("Refused")).toBeInTheDocument();
    expect(row.getAllByText("Replayed").length).toBeGreaterThan(0);
  },
};

// More decisions than a page: Next reads the rest after the last one shown, Previous goes back.
export const DecisionsPaged: Story = {
  parameters: { route: "/executions/950" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
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
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("This run recorded no decisions.")).toBeInTheDocument();
  },
};

// A run queued to replay an earlier run that asked afresh instead is marked so.
export const ReplayAbandoned: Story = {
  parameters: { route: "/executions/951" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("Replay abandoned")).toBeInTheDocument();
    expect(c.getByText("Abandoned: it could not be honoured, so the run asked afresh")).toBeInTheDocument();
    expect(c.getByRole("link", { name: "902" })).toHaveAttribute("href", "/executions/902");
  },
};

// The run's own log reads oldest first, with the text filters; no run filter or column.
export const RunLogOldestFirst: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
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
const nodeOf = (graph: HTMLElement, id: string) => graph.querySelector(`[data-node-id="${id}"]`) as HTMLElement;

// A failed run after a checkpoint: the checkpoint is marked (never what it holds), the failed step
// shows its failure, the routing step's tracks sit under it, and the steps it can resume at offer
// "Resume from here".
export const RunGraphCheckpoints: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(nodeOf(graph, "Findings#1")).toBeInTheDocument());
    const g = within(graph);
    expect(within(nodeOf(graph, "Findings#1")).getByText("checkpoint")).toBeInTheDocument();
    expect(nodeOf(graph, "Findings#1")).toHaveAttribute("data-kind", "CHECKPOINT");
    expect(within(nodeOf(graph, "Summarize#2")).getByText("failed")).toBeInTheDocument();
    expect(within(nodeOf(graph, "Summarize#2")).getByText("Transient · TimeoutException")).toBeInTheDocument();
    expect(g.getByRole("list", { name: "Tracks of Route#3" })).toBeInTheDocument();
    expect(g.getAllByRole("button", { name: "Resume from here" })).toHaveLength(2);
    expect(within(nodeOf(graph, "Fetch#0")).queryByRole("button")).not.toBeInTheDocument();
    expect(c.getByRole("button", { name: "Resume" })).toBeInTheDocument();
  },
};

// Resume queues a run that skips past the latest checkpoint, beside Re-queue.
export const ResumeAfterCheckpoint: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await userEvent.click(await c.findByRole("button", { name: "Resume" }));
      expect(await c.findByText("Execution queued to resume.")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// "Resume from here" resumes at that node.
export const ResumeFromNode: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      const graph = await graphOf(c);
      await waitFor(() => expect(nodeOf(graph, "Publish#4")).toBeInTheDocument());
      await userEvent.click(within(nodeOf(graph, "Publish#4")).getByRole("button", { name: "Resume from here" }));
      expect(await c.findByText("Execution queued to resume.")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A host that refuses the resume: its reason is shown and the page stays.
export const ResumeRefused: Story = {
  parameters: {
    route: "/executions/902",
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
// sit side by side, and a completed run offers no resume.
export const ResumedRunRestored: Story = {
  parameters: { route: "/executions/952" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(nodeOf(graph, "Fetch#0")).toBeInTheDocument());
    expect(nodeOf(graph, "Fetch#0")).toHaveAttribute("data-state", "RESTORED");
    expect(within(nodeOf(graph, "Findings#1")).getByText("restored")).toBeInTheDocument();
    expect(within(graph).getByRole("list", { name: "Branches of Signals#4, run side by side" })).toBeInTheDocument();
    expect(graph.querySelector('[data-track="Short"]')).toHaveAttribute("data-taken", "true");
    expect(within(graph).getByText("LegacyStep")).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    expect(within(graph).queryByRole("button", { name: "Resume from here" })).not.toBeInTheDocument();
  },
};

// A resumed run names the run it resumed, linked, and the node it resumed at.
export const ResumedRunNamesItsSource: Story = {
  parameters: { route: "/executions/952" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    const resumes = c.getByText("Resumes", { selector: "dt" }).nextElementSibling as HTMLElement;
    expect(within(resumes).getByRole("link", { name: "902" })).toHaveAttribute("href", "/executions/902");
    expect(resumes).toHaveTextContent("902 at Summarize#2");
  },
};

// A run that resumed after the latest checkpoint names no node.
export const ResumedAfterLatestCheckpoint: Story = {
  parameters: {
    route: "/executions/952",
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
    const resumes = c.getByText("Resumes", { selector: "dt" }).nextElementSibling as HTMLElement;
    expect(resumes).toHaveTextContent("902 after its latest checkpoint");
  },
};

// A run that was not resumed shows no Resumes field.
export const NotResumedHasNoResumesField: Story = {
  parameters: { route: "/executions/902" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument());
    expect(c.queryByText("Resumes", { selector: "dt" })).not.toBeInTheDocument();
  },
};

// A run with no saved input cannot be resumed, as it cannot be re-queued: the checkpoint still
// shows, but nothing offers a resume.
export const NoInputNoResume: Story = {
  parameters: { route: "/executions/954" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    await waitFor(() => expect(nodeOf(graph, "Findings#1")).toBeInTheDocument());
    expect(within(nodeOf(graph, "Findings#1")).getByText("checkpoint")).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    expect(within(graph).queryByRole("button", { name: "Resume from here" })).not.toBeInTheDocument();
  },
};

// A train this host has no declared graph for says so.
export const NoDeclaredGraph: Story = {
  parameters: { route: "/executions/900" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    expect(await within(graph).findByText(/no declared graph for this train/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
  },
};

// A run whose routing nests deeper than a query can follow nested tracks: every node is drawn, at
// any depth, under the track it sits on, and the failed step six tracks deep offers "Resume from
// here". A Parallel step's branches are lanes side by side, and a step the host stopped mid-junction
// shows as interrupted.
export const DeepRunGraph: Story = {
  parameters: { route: "/executions/956" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    const lanes = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0";
    const leaf = `${lanes}/Left/Check#0/Open/Pick#0/Deep/Leaf#0`;
    await waitFor(() => expect(nodeOf(graph, leaf)).toBeInTheDocument());
    // The leaf sits under the Deep track of Pick, under the Open track of Check, in the Left lane.
    const pick = nodeOf(graph, `${lanes}/Left/Check#0/Open/Pick#0`);
    expect(pick.querySelector('[data-track="Deep"]')!.contains(nodeOf(graph, leaf))).toBe(true);
    expect(nodeOf(graph, leaf)).toHaveAttribute("data-can-resume", "true");
    expect(within(nodeOf(graph, leaf)).getByRole("button", { name: "Resume from here" })).toBeInTheDocument();
    // The Parallel step's branches, as lanes.
    const parallel = nodeOf(graph, lanes);
    const branches = [...parallel.querySelectorAll(":scope > ul > [data-branch]")].map((b) => b.getAttribute("data-branch"));
    expect(branches).toEqual(["Left", "Right"]);
    expect(within(parallel).getByRole("list", { name: "Branches of Lanes#0, run side by side" })).toBeInTheDocument();
    const right = nodeOf(graph, `${lanes}/Right/Tally#0`);
    expect(right).toHaveAttribute("data-state", "INTERRUPTED");
    expect(within(right).getByText("interrupted")).toBeInTheDocument();
    // The track the run did not take, dimmed.
    expect(graph.querySelector('[data-track="Slow"]')).toHaveAttribute("data-taken", "false");
  },
};

// Resuming at the deep step names it.
export const ResumeFromDeepNode: Story = {
  parameters: { route: "/executions/956" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    const leaf = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0/Deep/Leaf#0";
    try {
      const graph = await graphOf(c);
      await waitFor(() => expect(nodeOf(graph, leaf)).toBeInTheDocument());
      await userEvent.click(within(nodeOf(graph, leaf)).getByRole("button", { name: "Resume from here" }));
      expect(await c.findByText("Execution queued to resume.")).toBeInTheDocument();
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
  parameters: { route: "/executions/902", mock: runGraphError },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await graphOf(c);
    expect(await within(graph).findByText(/Simulated run graph read failure/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
    expect(c.getByRole("heading", { name: "BetaJob" })).toBeInTheDocument();
  },
};
