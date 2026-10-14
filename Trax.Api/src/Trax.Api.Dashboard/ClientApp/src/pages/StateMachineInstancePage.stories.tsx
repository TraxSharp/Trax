import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { StateMachineInstancePage } from "./StateMachineInstancePage";
import { MACHINE_IDS, machineScenario } from "../mock/scenarios";

const system = (id: string) => `/state-machines/source-partition/system/${id}`;

const meta = {
  title: "Pages/State machine instance",
  component: StateMachineInstancePage,
  parameters: {
    routePath: "/state-machines/:machine/:owner/:id",
    route: system(MACHINE_IDS.dispatched),
    mock: machineScenario,
  },
} satisfies Meta<typeof StateMachineInstancePage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
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
    expect(field(c, "Waiting on a run")).toHaveTextContent(/^Yes/);
    expect(c.queryByText(/context/i, { selector: "dt" })).not.toBeInTheDocument();
    expect(runs(c).getByRole("link", { name: "7101" })).toHaveAttribute("href", "/executions/7101");
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
        "/work-queue/7201",
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
    expect(field(c, "Waiting on a run")).toHaveTextContent("No");
    expect(c.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(runs(c).getByText("Showing the newest 1 runs; the instance invoked more.")).toBeInTheDocument();
  },
};

// A user's draft is read-only to operators: no Cancel, and its runs say why only the live one is
// listed. Whose draft it is is never shown.
export const UserDraft: Story = {
  parameters: { route: `/state-machines/topic-map/user/${MACHINE_IDS.draftBuilding}?row=21` },
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
  parameters: { route: `/state-machines/topic-map/user/${MACHINE_IDS.draftBuilding}` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/named by its row as well as its id/)).toBeInTheDocument();
  },
};

// An owner segment other than system or user is refused.
export const UnknownOwner: Story = {
  parameters: { route: `/state-machines/topic-map/admin/${MACHINE_IDS.draftBuilding}` },
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
