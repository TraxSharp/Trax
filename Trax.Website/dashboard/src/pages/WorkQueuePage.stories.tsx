import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { WorkQueuePage } from "./WorkQueuePage";
import { emptyPage, errorOverride, workQueueScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/WorkQueue",
  component: WorkQueuePage,
  parameters: { route: "/work-queue", mock: workQueueScenario },
} satisfies Meta<typeof WorkQueuePage>;

export default meta;
type Story = StoryObj<typeof meta>;

// A status badge is a <span>; scope to it so the filter dropdown's <option> isn't counted.
const badge = { selector: "span" } as const;

// The cancel flows call window.confirm; auto-accept it for the duration of a play.
function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// Cancel a single queued row -> it flips to CANCELLED and a toast fires.
export const CancelSingle: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
      expect(c.getAllByText("Cancelled", badge)).toHaveLength(1); // only #605 to start
      const row = c.getByLabelText("Select #601").closest("tr") as HTMLElement;
      await userEvent.click(within(row).getByText("Cancel"));
      await waitFor(() => expect(c.getAllByText("Cancelled", badge)).toHaveLength(2));
      expect(await c.findByText(/Entry #601 cancelled/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Select two queued rows and bulk-cancel them.
export const CancelSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
      expect(c.getAllByText("Cancelled", badge)).toHaveLength(1);
      await userEvent.click(c.getByLabelText("Select #601"));
      await userEvent.click(c.getByLabelText("Select #602"));
      await userEvent.click(c.getByText("Cancel selected"));
      await waitFor(() => expect(c.getAllByText("Cancelled", badge)).toHaveLength(3));
    } finally {
      restore();
    }
  },
};

// Select-all checks every queued row; Clear resets the selection.
export const SelectAllAndClear: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    // Wait for rows to load (the header "Select all" exists even when empty).
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select all"));
    // "{n} selected" is split across text nodes; a regex matches the element's text.
    await waitFor(() => expect(c.getByText(/3 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Clear"));
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
  },
};

// The status filter narrows the list to matching rows.
export const FilterByStatus: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "CANCELLED");
    await waitFor(() => expect(c.queryByLabelText("Select #601")).not.toBeInTheDocument());
    expect(c.getByText("ArchiveTrain")).toBeInTheDocument();
  },
};

// The Queue train button opens the dialog; its Cancel closes it.
export const QueueTrainDialogFlow: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByRole("button", { name: "Queue train" }));
    await waitFor(() => expect(c.getByText("Queue a train")).toBeInTheDocument());
    const dialog = c.getByText("Queue a train").closest("div") as HTMLElement;
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(c.queryByText("Queue a train")).not.toBeInTheDocument());
  },
};

// Combining the train-name filter with the status filter narrows to the intersection:
// "Order" leaves #601 (QUEUED) + #604 (DISPATCHED), then status=QUEUED leaves only #601.
export const FilterByStatusAndTrain: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by train name…"), "Order");
    await waitFor(() => expect(c.queryByLabelText("Select #602")).not.toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "QUEUED");
    await waitFor(() => expect(c.getAllByLabelText(/^Select #\d+$/)).toHaveLength(1));
    expect(c.getByLabelText("Select #601")).toBeInTheDocument();
    expect(c.queryAllByText("Dispatched", badge)).toHaveLength(0); // #604 excluded by status
  },
};

// A filter combination with no matching rows falls through to the empty state (not an error):
// there is no CANCELLED OrderTrain entry.
export const FilterCombinationYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "CANCELLED");
    await userEvent.type(c.getByPlaceholderText("Filter by train name…"), "Order");
    await waitFor(() => expect(c.getByText("No entries.")).toBeInTheDocument());
  },
};

// Only QUEUED rows are actionable: DISPATCHED (#604) and CANCELLED (#605) render but have no
// checkbox and no Cancel button.
export const OnlyActionableRowsSelectable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    expect(c.getAllByLabelText(/^Select #\d+$/)).toHaveLength(3);
    expect(c.getAllByRole("button", { name: "Cancel" })).toHaveLength(3);
    expect(c.getAllByText("Dispatched", badge)).toHaveLength(1);
    expect(c.getAllByText("Cancelled", badge)).toHaveLength(1);
    expect(c.queryByLabelText("Select #604")).not.toBeInTheDocument();
    expect(c.queryByLabelText("Select #605")).not.toBeInTheDocument();
  },
};

// Changing a filter clears the current selection (consistent with the dead-letters page), so a
// bulk action can never target rows the filter has hidden.
export const SelectionClearsOnFilter: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select #601"));
    await waitFor(() => expect(c.getByText(/1 selected/)).toBeInTheDocument());
    // Status filter change: selection is dropped.
    await userEvent.selectOptions(c.getByRole("combobox"), "QUEUED");
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
    // Same for the train-name filter.
    await userEvent.click(c.getByLabelText("Select #601"));
    await waitFor(() => expect(c.getByText(/1 selected/)).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by train name…"), "Order");
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
  },
};

// The "Select all" header checkbox reflects whether every queued row is selected: unchecked on
// partial selection, checked only when all three are, and back to unchecked when one is removed.
export const PartialSelectionHeaderState: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    const header = c.getByLabelText("Select all") as HTMLInputElement;
    expect(header.checked).toBe(false);
    await userEvent.click(c.getByLabelText("Select #601"));
    expect(header.checked).toBe(false);
    await userEvent.click(c.getByLabelText("Select #602"));
    await userEvent.click(c.getByLabelText("Select #603"));
    await waitFor(() => expect(header.checked).toBe(true));
    await userEvent.click(c.getByLabelText("Select #601"));
    await waitFor(() => expect(header.checked).toBe(false));
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("WorkQueueQueries", "workQueues") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No entries.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("WorkQueueQueries", "workQueues") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

// The Subject column shows an entry's subject key, and the Confirmed column marks an entry a
// two-phase enqueue has staged but not confirmed.
export const SubjectAndStaged: Story = {
  parameters: { mock: workQueueScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
    const staged = c.getByLabelText("Select #602").closest("tr") as HTMLElement;
    expect(within(staged).getByText("Staged")).toBeInTheDocument();
    expect(c.getAllByText("order-42")).toHaveLength(2); // 601 + 604
    const confirmed = c.getByLabelText("Select #601").closest("tr") as HTMLElement;
    expect(within(confirmed).getByText("Yes")).toBeInTheDocument();
    expect(c.getAllByText("Staged")).toHaveLength(1); // 605 is cancelled, not staged
  },
};

// Clicking a subject shows that subject's queue (subjectKey, matched exactly).
export const FilterBySubject: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #603")).toBeInTheDocument());
    await userEvent.click(c.getAllByRole("button", { name: "order-42" })[0]);
    await waitFor(() => expect(c.queryByLabelText("Select #603")).not.toBeInTheDocument());
    expect(c.getByLabelText("Subject")).toHaveValue("order-42");
    expect(c.getByLabelText("Select #601")).toBeInTheDocument();
    expect(c.getAllByRole("button", { name: "order-42" })).toHaveLength(2); // 601 and 604
  },
};

// The manifest filter narrows to one manifest's entries; it can arrive in the URL.
export const FilterByManifest: Story = {
  parameters: { route: "/work-queue?manifestId=42" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByLabelText("Manifest id")).toHaveValue("42");
    await waitFor(() => expect(c.getByLabelText("Select #602")).toBeInTheDocument());
    expect(c.queryByLabelText("Select #601")).not.toBeInTheDocument();
    await userEvent.clear(c.getByLabelText("Manifest id"));
    await waitFor(() => expect(c.getByLabelText("Select #601")).toBeInTheDocument());
  },
};

// The Replays column links the run whose decisions an entry's run will replay.
export const ReplaysColumn: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #603")).toBeInTheDocument());
    const row = c.getByLabelText("Select #603").closest("tr") as HTMLElement;
    expect(within(row).getByRole("link", { name: "#899" })).toHaveAttribute("href", "/executions/899");
  },
};
