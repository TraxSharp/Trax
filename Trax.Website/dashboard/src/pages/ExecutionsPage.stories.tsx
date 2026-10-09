import type { Meta, StoryObj } from "@storybook/react-vite";
import { fireEvent } from "@testing-library/react";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ExecutionsPage } from "./ExecutionsPage";
import { emptyPage, errorOverride, executionScenario } from "../mock/scenarios";
import { USER_OWNED_RUN_CANCEL_REFUSAL, userDraftRunsOverlay } from "../mock/store/overlays";

const meta = {
  title: "Pages/Executions",
  component: ExecutionsPage,
  parameters: { route: "/executions", mock: executionScenario },
} satisfies Meta<typeof ExecutionsPage>;

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

export const Real: Story = { parameters: { real: true } };

// Train-state filter (1st select) narrows the table.
export const FilterByState: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "FAILED");
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("BetaJob")).toBeInTheDocument();
  },
};

// Train-name filter narrows the table.
export const FilterByName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by train name…"), "Gamma");
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("GammaJob")).toBeInTheDocument();
  },
};

// Order select reverses the list.
export const SortOldest: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    expect(c.getAllByRole("link")[0]).toHaveTextContent("AlphaJob"); // newest first
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "OLDEST");
    await waitFor(() => expect(c.getAllByRole("link")[0]).toHaveTextContent("GammaJob"));
  },
};

// Keyset pagination: Next loads page 2, Previous returns.
export const Pagination: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(c.getByText("DeltaJob")).toBeInTheDocument());
    expect(c.queryByText("AlphaJob")).not.toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Previous" }));
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
  },
};

// Setting a time-range reveals Clear range, which resets it.
export const TimeRange: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    expect(c.queryByText("Clear range")).not.toBeInTheDocument();
    // datetime-local: set the value directly (native pickers don't respond to typing).
    fireEvent.change(c.getByLabelText("After"), { target: { value: "2026-07-01T10:00" } });
    await waitFor(() => expect(c.getByText("Clear range")).toBeInTheDocument());
    await userEvent.click(c.getByText("Clear range"));
    await waitFor(() => expect(c.queryByText("Clear range")).not.toBeInTheDocument());
  },
};

// The live feed animates from the synthetic event simulator (opt-in).
export const LiveFeed: Story = {
  parameters: { simulate: true },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Live feed")).toBeInTheDocument());
    await waitFor(
      () => expect(c.getAllByText(/OrderTrain|EmailTrain|ReportTrain/).length).toBeGreaterThan(0),
      { timeout: 5000 },
    );
  },
};

// State + name filters compose: COMPLETED + "Alpha" leaves only AlphaJob.
export const FilterByStateAndName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "COMPLETED");
    await userEvent.type(c.getByPlaceholderText("Filter by train name…"), "Alpha");
    await waitFor(() => expect(c.queryByText("BetaJob")).not.toBeInTheDocument());
    expect(c.getByText("AlphaJob")).toBeInTheDocument();
    expect(c.queryByText("GammaJob")).not.toBeInTheDocument();
  },
};

// A filter that matches nothing shows the empty state, not an error (no PENDING executions exist).
export const FilterYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "PENDING");
    await waitFor(() => expect(c.getByText("No executions.")).toBeInTheDocument());
  },
};

// Pager disabled states across the keyset boundary: Previous is disabled on the first page,
// Next is disabled on the last (no nextCursor), and returning re-disables Previous.
export const PaginationBoundaries: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    const prev = () => c.getByRole("button", { name: "Previous" });
    const next = () => c.getByRole("button", { name: "Next" });
    expect(prev()).toBeDisabled();
    expect(next()).toBeEnabled();
    await userEvent.click(next());
    await waitFor(() => expect(c.getByText("DeltaJob")).toBeInTheDocument());
    expect(next()).toBeDisabled();
    expect(prev()).toBeEnabled();
    await userEvent.click(prev());
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    expect(prev()).toBeDisabled();
  },
};

// Applying a filter while on page 2 resets to the first page of the filtered results.
export const FilterResetsToFirstPage: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(c.getByText("DeltaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "FAILED");
    await waitFor(() => expect(c.getByText("BetaJob")).toBeInTheDocument());
    expect(c.queryByText("DeltaJob")).not.toBeInTheDocument();
    expect(c.getByRole("button", { name: "Previous" })).toBeDisabled(); // reset to first page
  },
};

// Duration renders seconds for a finished run and a dash for one still in progress.
export const DurationFormatting: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    const alpha = c.getByText("AlphaJob").closest("tr") as HTMLElement;
    expect(within(alpha).getByText("12.0 s")).toBeInTheDocument(); // COMPLETED, 12s
    const gamma = c.getByText("GammaJob").closest("tr") as HTMLElement;
    expect(within(gamma).getByText("—")).toBeInTheDocument(); // IN_PROGRESS, no end time
  },
};

// Only PENDING / IN_PROGRESS rows are selectable; bulk-cancel sends them in one cancelExecutions.
export const BulkCancel: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
      // 903 COMPLETED and 902 FAILED are not actionable; only 901 (IN_PROGRESS) is.
      expect(c.queryByLabelText("Select #903")).not.toBeInTheDocument();
      await userEvent.click(c.getByLabelText("Select #901"));
      await waitFor(() => expect(c.getByText(/1 selected/)).toBeInTheDocument());
      await userEvent.click(c.getByText("Cancel selected"));
      expect(await c.findByText(/Cancellation requested for 1/)).toBeInTheDocument();
      await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// A selection of only runs users' drafts started is refused with the API's reason.
export const BulkCancelUserDraftRunRefused: Story = {
  parameters: { overlays: [userDraftRunsOverlay([901])] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #901")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #901"));
      await userEvent.click(c.getByText("Cancel selected"));
      expect(await c.findByText(USER_OWNED_RUN_CANCEL_REFUSAL)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("OperationsQueries", "executions") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No executions.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("OperationsQueries", "executions") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

// The failure-class filter narrows the grid server-side to runs that failed that way.
export const FilterByFailureClass: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByLabelText("Failure class"), "TRANSIENT");
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("BetaJob")).toBeInTheDocument();
  },
};

// A failure class nothing failed with leaves the grid empty.
export const FailureClassYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByLabelText("Failure class"), "PERMANENT");
    await waitFor(() => expect(c.getByText("No executions.")).toBeInTheDocument());
  },
};

// The failure-class column names how a failed run failed; other rows leave it blank.
export const FailureClassColumn: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("BetaJob")).toBeInTheDocument());
    const beta = c.getByText("BetaJob").closest("tr") as HTMLElement;
    expect(within(beta).getByText("Transient")).toBeInTheDocument();
    const alpha = c.getByText("AlphaJob").closest("tr") as HTMLElement;
    expect(within(alpha).queryByText("Transient")).not.toBeInTheDocument();
  },
};

// The exact-match filters the API serves: one run by external id, a run's children, a host's runs.
export const FilterByExternalId: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("External id"), "exec-902");
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("BetaJob")).toBeInTheDocument();
  },
};

export const FilterByParentId: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Parent id"), "8x00");
    expect(c.getByLabelText("Parent id")).toHaveValue("800"); // digits only
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("BetaJob")).toBeInTheDocument();
  },
};

export const FilterByHostName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Host name"), "gamma-host");
    await waitFor(() => expect(c.queryByText("AlphaJob")).not.toBeInTheDocument());
    expect(c.getByText("GammaJob")).toBeInTheDocument();
  },
};

// An exact filter that matches no run shows the empty state.
export const ExactFilterYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaJob")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Host name"), "no-such-host");
    await waitFor(() => expect(c.getByText("No executions.")).toBeInTheDocument());
  },
};

// The parent column links a child run to the run that started it; a running run shows the
// junction it is running.
export const ParentAndRunningJunctionColumns: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("BetaJob")).toBeInTheDocument());
    const beta = c.getByText("BetaJob").closest("tr") as HTMLElement;
    expect(within(beta).getByRole("link", { name: "#800" })).toHaveAttribute("href", "/executions/800");
    const gamma = c.getByText("GammaJob").closest("tr") as HTMLElement;
    expect(within(gamma).getByText("ProcessStep")).toBeInTheDocument();
    const alpha = c.getByText("AlphaJob").closest("tr") as HTMLElement;
    expect(within(alpha).queryByText("ProcessStep")).not.toBeInTheDocument();
  },
};
