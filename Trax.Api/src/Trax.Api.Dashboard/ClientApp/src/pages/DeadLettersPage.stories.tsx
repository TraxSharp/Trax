import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { DeadLettersPage } from "./DeadLettersPage";
import { deadLetterScenario, emptyPage, errorOverride } from "../mock/scenarios";

const meta = {
  title: "Pages/Dead letters",
  component: DeadLettersPage,
  parameters: { route: "/dead-letters", mock: deadLetterScenario },
} satisfies Meta<typeof DeadLettersPage>;

export default meta;
type Story = StoryObj<typeof meta>;

const badge = { selector: "span" } as const;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

// The open modal card (the parent of the header that holds `title`).
function modalOf(c: ReturnType<typeof within>, title: string): HTMLElement {
  return c.getByText(title).closest("div")!.parentElement as HTMLElement;
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// Requeue a single awaiting row -> RETRIED + toast.
export const RequeueSingle: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
      expect(c.getAllByText("Retried", badge)).toHaveLength(1); // only #704 to start
      const row = c.getByLabelText("Select #701").closest("tr") as HTMLElement;
      await userEvent.click(within(row).getByText("Requeue"));
      await waitFor(() => expect(c.getAllByText("Retried", badge)).toHaveLength(2));
      expect(await c.findByText(/Dead letter #701 requeued/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Acknowledge a single row through the note modal -> ACKNOWLEDGED + toast.
export const AcknowledgeSingle: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    expect(c.getAllByText("Acknowledged", badge)).toHaveLength(1); // only #705
    const row = c.getByLabelText("Select #701").closest("tr") as HTMLElement;
    await userEvent.click(within(row).getByText("Acknowledge"));
    await waitFor(() => expect(c.getByText("Acknowledge dead letter #701")).toBeInTheDocument());
    const modal = modalOf(c, "Acknowledge dead letter #701");
    await userEvent.type(within(modal).getByPlaceholderText("Reason / note…"), "resolved by hand");
    await userEvent.click(within(modal).getByRole("button", { name: "Acknowledge" }));
    await waitFor(() => expect(c.getAllByText("Acknowledged", badge)).toHaveLength(2));
    expect(await c.findByText(/Dead letter #701 acknowledged/)).toBeInTheDocument();
  },
};

// Cancelling the note modal makes no change.
export const NoteModalCancel: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    const row = c.getByLabelText("Select #701").closest("tr") as HTMLElement;
    await userEvent.click(within(row).getByText("Acknowledge"));
    await waitFor(() => expect(c.getByText("Acknowledge dead letter #701")).toBeInTheDocument());
    const modal = modalOf(c, "Acknowledge dead letter #701");
    await userEvent.click(within(modal).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(c.queryByText("Acknowledge dead letter #701")).not.toBeInTheDocument());
    expect(c.getByLabelText("Select #701")).toBeInTheDocument(); // still awaiting
  },
};

// Bulk-requeue two selected rows.
export const RequeueSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
      expect(c.getAllByText("Retried", badge)).toHaveLength(1);
      await userEvent.click(c.getByLabelText("Select #701"));
      await userEvent.click(c.getByLabelText("Select #702"));
      await userEvent.click(c.getByText("Requeue selected"));
      await waitFor(() => expect(c.getAllByText("Retried", badge)).toHaveLength(3));
    } finally {
      restore();
    }
  },
};

// Bulk-acknowledge two selected rows through the note modal.
export const AcknowledgeSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    expect(c.getAllByText("Acknowledged", badge)).toHaveLength(1);
    await userEvent.click(c.getByLabelText("Select #701"));
    await userEvent.click(c.getByLabelText("Select #702"));
    await userEvent.click(c.getByText("Acknowledge selected"));
    await waitFor(() => expect(c.getByText("Acknowledge 2 dead letter(s)")).toBeInTheDocument());
    const modal = modalOf(c, "Acknowledge 2 dead letter(s)");
    await userEvent.type(within(modal).getByPlaceholderText("Reason / note…"), "bulk resolved");
    await userEvent.click(within(modal).getByRole("button", { name: "Acknowledge" }));
    await waitFor(() => expect(c.getAllByText("Acknowledged", badge)).toHaveLength(3));
  },
};

// "Requeue all" flips every awaiting row to Retried (no selection needed).
export const RequeueAll: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
      expect(c.getAllByText("Retried", badge)).toHaveLength(1); // only #704 to start
      await userEvent.click(c.getByRole("button", { name: "Requeue all" }));
      await waitFor(() => expect(c.getAllByText("Retried", badge)).toHaveLength(4)); // + 701/702/703
    } finally {
      restore();
    }
  },
};

// "Acknowledge all" opens the note modal, then flips every awaiting row to Acknowledged.
export const AcknowledgeAll: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    expect(c.getAllByText("Acknowledged", badge)).toHaveLength(1); // only #705
    await userEvent.click(c.getByRole("button", { name: "Acknowledge all" }));
    await waitFor(() =>
      expect(c.getByText("Acknowledge every unresolved dead letter")).toBeInTheDocument(),
    );
    const modal = modalOf(c, "Acknowledge every unresolved dead letter");
    await userEvent.type(within(modal).getByPlaceholderText("Reason / note…"), "closing out all");
    await userEvent.click(within(modal).getByRole("button", { name: "Acknowledge" }));
    await waitFor(() => expect(c.getAllByText("Acknowledged", badge)).toHaveLength(4));
  },
};

export const SelectAllAndClear: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select all"));
    await waitFor(() => expect(c.getByText(/3 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Clear"));
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
  },
};

export const FilterByStatus: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "ACKNOWLEDGED");
    await waitFor(() => expect(c.queryByLabelText("Select #701")).not.toBeInTheDocument());
    expect(c.getByText("ArchiveManifest")).toBeInTheDocument();
  },
};

// Filtering to a resolved status leaves only non-actionable rows: no per-row checkboxes, no
// Requeue/Acknowledge buttons, and the "Select all" header is disabled.
export const FilterToNonActionable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "RETRIED");
    await waitFor(() => expect(c.queryByLabelText("Select #701")).not.toBeInTheDocument());
    expect(c.getByText("OrderManifest")).toBeInTheDocument(); // #704 RETRIED
    expect(c.queryAllByLabelText(/^Select #\d+$/)).toHaveLength(0);
    expect(c.queryByText("Requeue")).not.toBeInTheDocument();
    expect(c.queryByText("Acknowledge")).not.toBeInTheDocument();
    expect(c.getByLabelText("Select all")).toBeDisabled();
  },
};

// Changing the status filter clears the current selection (the page calls clear() on change).
export const SelectionClearsOnFilter: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select #701"));
    await waitFor(() => expect(c.getByText(/1 selected/)).toBeInTheDocument());
    await userEvent.selectOptions(c.getByRole("combobox"), "AWAITING_INTERVENTION");
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
    expect(c.getByLabelText("Select #701")).toBeInTheDocument(); // rows still shown, just deselected
  },
};

// The header checkbox reflects whether all three awaiting rows are selected.
export const PartialSelectionHeaderState: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    const header = c.getByLabelText("Select all") as HTMLInputElement;
    expect(header.checked).toBe(false);
    await userEvent.click(c.getByLabelText("Select #701"));
    expect(header.checked).toBe(false);
    await userEvent.click(c.getByLabelText("Select #702"));
    await userEvent.click(c.getByLabelText("Select #703"));
    await waitFor(() => expect(header.checked).toBe(true));
    await userEvent.click(c.getByLabelText("Select #702"));
    await waitFor(() => expect(header.checked).toBe(false));
  },
};

// In the note modal the Acknowledge button is disabled until a non-blank reason is entered.
export const AckModalRequiresNote: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
    const row = c.getByLabelText("Select #701").closest("tr") as HTMLElement;
    await userEvent.click(within(row).getByText("Acknowledge"));
    await waitFor(() => expect(c.getByText("Acknowledge dead letter #701")).toBeInTheDocument());
    const modal = modalOf(c, "Acknowledge dead letter #701");
    const confirm = within(modal).getByRole("button", { name: "Acknowledge" });
    expect(confirm).toBeDisabled();
    await userEvent.type(within(modal).getByPlaceholderText("Reason / note…"), "   ");
    expect(confirm).toBeDisabled(); // whitespace only
    await userEvent.type(within(modal).getByPlaceholderText("Reason / note…"), "closed as duplicate");
    await waitFor(() => expect(confirm).toBeEnabled());
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("DeadLetterQueries", "deadLetters") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No dead letters.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("DeadLetterQueries", "deadLetters") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

// "Requeue all, ask afresh" resolves every awaiting row the same way, with askAfresh.
export const RequeueAllAskAfresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Requeue all, ask afresh" }));
      await waitFor(() => expect(c.getAllByText("Retried", badge)).toHaveLength(4));
    } finally {
      restore();
    }
  },
};

// "Requeue selected, ask afresh" requeues only the selection.
export const RequeueSelectedAskAfresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #701")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #703"));
      await userEvent.click(c.getByText("Requeue selected, ask afresh"));
      await waitFor(() => expect(c.getAllByText("Retried", badge)).toHaveLength(2));
      expect(c.getByLabelText("Select #701")).toBeInTheDocument(); // still awaiting
    } finally {
      restore();
    }
  },
};

// The manifest filter narrows to one manifest's dead letters; the detail page links here with it.
export const FilterByManifest: Story = {
  parameters: { route: "/dead-letters?manifestId=41" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByLabelText("Manifest id")).toHaveValue("41");
    await waitFor(() => expect(c.getAllByText("OrderManifest").length).toBe(2)); // 701 and 704
    expect(c.queryByText("EmailManifest")).not.toBeInTheDocument();
    await userEvent.clear(c.getByLabelText("Manifest id"));
    await userEvent.type(c.getByLabelText("Manifest id"), "999");
    await waitFor(() => expect(c.getByText("No dead letters.")).toBeInTheDocument());
  },
};
