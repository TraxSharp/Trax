import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { StateMachineInstancePage } from "./StateMachineInstancePage";
import { MACHINE_IDS, machineScenario } from "../mock/scenarios";
import { setAutoConfirm } from "../lib/confirm";

const system = (id: string) => `/machines/source-partition/system/${id}`;

const meta = {
  title: "Pages/State machine instance",
  component: StateMachineInstancePage,
  parameters: {
    routePath: "/machines/:machine/:owner/:id",
    route: system(MACHINE_IDS.dispatched),
    mock: machineScenario,
  },
} satisfies Meta<typeof StateMachineInstancePage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  setAutoConfirm(true);
  return () => {
    setAutoConfirm(null);
  };
}

const runs = (c: ReturnType<typeof within>) => within(c.getByRole("region", { name: "Invoked runs" }));
const field = (c: ReturnType<typeof within>, label: string) => c.getByText(label, { selector: "dt" }).nextElementSibling;

export const Mock: Story = {};

// A system instance waiting on a dispatched run: its details (no context), its runs newest first
// with the live one marked and each linked to its run page, and the Cancel button.
export const SystemInstance: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "source-partition" })).toBeInTheDocument();
    expect(field(c, "Owner")).toHaveTextContent("System");
    expect(field(c, "State")).toHaveTextContent("Ingesting");
    expect(field(c, "Waiting on")).toHaveTextContent("Run 7101");
    expect(field(c, "Stuck")).toHaveTextContent("No");
    expect(c.queryByText(/context/i, { selector: "dt" })).not.toBeInTheDocument();
    expect(runs(c).getByRole("link", { name: "7101" })).toHaveAttribute("href", "/runs/7101");
    const rows = runs(c).getAllByRole("row").slice(1);
    expect(rows.map((r) => r.getAttribute("data-run-id"))).toEqual(["7101", "7090"]);
    expect(within(rows[0]).getByText("Live")).toBeInTheDocument();
    expect(within(rows[1]).queryByText("Live")).not.toBeInTheDocument();
    expect(c.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  },
};

// Cancel requests the dispatched run's cancel, in the API's words, and the run shows it.
export const CancelDispatchedRun: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await userEvent.click(await c.findByRole("button", { name: "Cancel" }));
      expect(await c.findByText(/Cancellation requested for the run instance/)).toBeInTheDocument();
      await waitFor(() => expect(runs(c).getByText("Cancellation requested")).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// An instance whose run is still queued links its work queue entry; Cancel stops the run before it
// starts.
export const CancelQueuedRun: Story = {
  parameters: { route: system(MACHINE_IDS.queued) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await c.findByRole("region", { name: "Invoked runs" });
      expect(runs(c).getByRole("link", { name: "work queue entry 7201" })).toHaveAttribute(
        "href",
        "/runs/queued/7201",
      );
      expect(runs(c).getByText("No runs to show.")).toBeInTheDocument();
      await userEvent.click(c.getByRole("button", { name: "Cancel" }));
      expect(await c.findByText(/is cancelled and will not start/)).toBeInTheDocument();
      await waitFor(() => expect(runs(c).queryByText(/work queue entry/)).not.toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// An instance that waits on no run offers no Cancel, and says when it invoked more runs than shown.
export const IdleInstance: Story = {
  parameters: { route: system(MACHINE_IDS.idle) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "source-partition" })).toBeInTheDocument();
    expect(field(c, "Waiting on")).toHaveTextContent("No run");
    expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(runs(c).getByText("Showing the newest 1 runs; the instance invoked more.")).toBeInTheDocument();
  },
};

// The machine drawn with the instance's state lit, and the commands that state accepts listed,
// read-only: the console sends none.
export const WhereItIs: Story = {
  parameters: { route: system(MACHINE_IDS.idle) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = await c.findByRole("group", { name: "States of source-partition" });
    await waitFor(() =>
      expect(graph.querySelector('[aria-current="step"]')?.firstChild?.textContent).toBe("Ingested"),
    );
    expect(c.getByText("No command moves it on from Ingested.")).toBeInTheDocument();
    expect(c.getByRole("region", { name: "History" })).toBeInTheDocument();
  },
};

// A draft whose timer should have fired: its due time, overdue, and stuck; the commands its state
// accepts with the guard, and the timer that moves it on.
export const OverdueTimer: Story = {
  parameters: { route: `/machines/topic-map/user/${MACHINE_IDS.draftIdle}?row=22` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "topic-map" })).toBeInTheDocument();
    expect(field(c, "Timer due")).toHaveTextContent("Overdue");
    expect(field(c, "Stuck")).toHaveTextContent("its timer should have fired");
    const commands = await c.findByRole("list", { name: "Commands it accepts" });
    expect(within(commands).getByText("Build")).toBeInTheDocument();
    expect(within(commands).getByText(/Choose a range of at least one day/)).toBeInTheDocument();
    expect(c.getByText(/After 1 day in ChoosingRange it moves to Expired/)).toBeInTheDocument();
  },
};

// An instance whose queued run is still waiting links its queue entry and shows where it is.
export const WaitingOnAQueuedRun: Story = {
  parameters: { route: system(MACHINE_IDS.queued) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByRole("heading", { name: "source-partition" });
    const waiting = field(c, "Waiting on")!;
    expect(within(waiting as HTMLElement).getByRole("link", { name: "Queue entry 7201" })).toHaveAttribute(
      "href",
      "/runs/queued/7201",
    );
    expect(waiting).toHaveTextContent("Pending");
    expect(c.getByText("When the run Ingesting started is done, it moves to Ingested.")).toBeInTheDocument();
  },
};

// A user's draft is read-only to operators: no Cancel, and its runs say why only the live one is
// listed. Whose draft it is is never shown.
export const UserDraft: Story = {
  parameters: { route: `/machines/topic-map/user/${MACHINE_IDS.draftBuilding}?row=21` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "topic-map" })).toBeInTheDocument();
    expect(field(c, "Owner")).toHaveTextContent("A user");
    expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(runs(c).getByText(/A user's draft lists only the run its state waits on/)).toBeInTheDocument();
  },
};

// A user's draft named without its row is refused before any read.
export const UserDraftWithoutRow: Story = {
  parameters: { route: `/machines/topic-map/user/${MACHINE_IDS.draftBuilding}` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/named by its row as well as its id/)).toBeInTheDocument();
  },
};

// An owner segment other than system or user is refused.
export const UnknownOwner: Story = {
  parameters: { route: `/machines/topic-map/admin/${MACHINE_IDS.draftBuilding}` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("'admin' is not an owner: an instance is owned by the system or by a user.")).toBeInTheDocument();
  },
};

// No such instance.
export const NotFound: Story = {
  parameters: { route: system("3f2c1a00-0000-4000-8000-0000000000ff") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "Instance not found" })).toBeInTheDocument();
  },
};
