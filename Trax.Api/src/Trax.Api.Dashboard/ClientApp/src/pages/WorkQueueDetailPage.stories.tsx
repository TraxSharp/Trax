import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { WorkQueueDetailPage } from "./WorkQueueDetailPage";
import { errorOverride, workQueueScenario } from "../mock/scenarios";
import { USER_OWNED_RUN_CANCEL_REFUSAL, userDraftRunsOverlay } from "../mock/store/overlays";

const meta = {
  title: "Pages/Work queue detail",
  component: WorkQueueDetailPage,
  // routePath lets the page read its id from useParams.
  parameters: { routePath: "/work-queue/:id", route: "/work-queue/601", mock: workQueueScenario },
} satisfies Meta<typeof WorkQueueDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

// The captured real-data fixture (route selects a fixtured id).
export const Fixture: Story = { parameters: { fixtures: true, mock: undefined, route: "/work-queue/1500000" } };
export const Real: Story = { parameters: { real: true } };

// A subject-keyed entry names the entry it waits on, and links it.
export const WaitingOnSubject: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderTrain" })).toBeInTheDocument());
    expect(c.getByText("Subject").nextElementSibling).toHaveTextContent("order-42");
    expect(c.getByText("Waiting on").nextElementSibling).toHaveTextContent(
      "Entry 604, which is running for the same subject",
    );
    expect(c.getByRole("link", { name: "Entry 604" })).toHaveAttribute("href", "/work-queue/604");
  },
};

// The input is shown as the API returns it, sensitive values masked.
export const MaskedInput: Story = {
  parameters: { route: "/work-queue/603" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "ReportTrain" })).toBeInTheDocument());
    expect(c.getByText(/"cardNumber": "\[REDACTED\]"/)).toBeInTheDocument();
    expect(c.queryByText("Waiting on")).not.toBeInTheDocument();
  },
};

// A staged entry says it is waiting on its OnQueue hook.
export const Staged: Story = {
  parameters: { route: "/work-queue/602" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "EmailTrain" })).toBeInTheDocument());
    expect(c.getByText("Confirmed at").nextElementSibling).toHaveTextContent(
      "Not yet: staged until its OnQueue hook returns",
    );
    expect(c.queryByText("Input")).not.toBeInTheDocument();
  },
};

// Cancel a queued entry: the status reads back Cancelled and the button goes.
export const CancelEntry: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Cancel entry" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Cancel entry" }));
      await waitFor(() => expect(c.getByText("Status").nextElementSibling).toHaveTextContent("Cancelled"));
      expect(c.queryByRole("button", { name: "Cancel entry" })).not.toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// An entry a step of a user's state-machine draft queued is read-only to operators: its cancel is
// refused with the API's reason, and it stays queued.
export const CancelUserDraftEntryRefused: Story = {
  parameters: { overlays: [userDraftRunsOverlay([], [601])] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Cancel entry" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Cancel entry" }));
      expect(await c.findByText(USER_OWNED_RUN_CANCEL_REFUSAL)).toBeInTheDocument();
      expect(c.getByText("Status").nextElementSibling).toHaveTextContent("Queued");
    } finally {
      restore();
    }
  },
};

// A dispatched entry cannot be cancelled and links its run.
export const DispatchedHasNoCancel: Story = {
  parameters: { route: "/work-queue/604" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderTrain" })).toBeInTheDocument());
    expect(c.queryByRole("button", { name: "Cancel entry" })).not.toBeInTheDocument();
    expect(c.getByRole("link", { name: "#88001" })).toHaveAttribute("href", "/executions/88001");
  },
};

export const NotFound: Story = {
  parameters: { route: "/work-queue/999" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No work queue entry found.")).toBeInTheDocument();
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("WorkQueueQueries", "detail") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};

// An entry whose run replays an earlier run's decisions links that run; the subject links its queue.
export const ReplaysDecisions: Story = {
  parameters: { route: "/work-queue/603" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("link", { name: "#899" })).toHaveAttribute("href", "/executions/899");
  },
};

export const SubjectLinksItsQueue: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("link", { name: "order-42" })).toHaveAttribute("href", "/work-queue?subjectKey=order-42");
    expect(c.getByText("None: it asks afresh")).toBeInTheDocument();
  },
};
