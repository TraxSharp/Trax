import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ManifestGroupsPage } from "./ManifestGroupsPage";
import { emptyPage, errorOverride, groupScenario, groupScenarioWithRetired } from "../mock/scenarios";

const meta = {
  title: "Pages/Manifest groups",
  component: ManifestGroupsPage,
  parameters: { route: "/groups", mock: groupScenario },
} satisfies Meta<typeof ManifestGroupsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};
export const Real: Story = { parameters: { real: true } };

// The name filter narrows the list. Group names also appear as nodes in the dependency graph, so
// scope every group-name assertion to the grid table.
export const FilterByName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    await waitFor(() => expect(table().getByText("alpha-group")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by name…"), "beta");
    await waitFor(() => expect(table().queryByText("alpha-group")).not.toBeInTheDocument());
    expect(table().getByText("beta-group")).toBeInTheDocument();
  },
};

// Each row shows the batched per-group stats (manifests / runs / failed) for the visible page.
export const PerGroupStats: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    await waitFor(() => expect(table().getByText("alpha-group")).toBeInTheDocument());
    const row = table().getByText("alpha-group").closest("tr")!;
    // alpha: manifests 6, runs 100, failed 7 (loaded by the batched stats query).
    await waitFor(() => expect(within(row).getByText("6")).toBeInTheDocument());
    expect(within(row).getByText("100")).toBeInTheDocument();
    expect(within(row).getByText("7")).toBeInTheDocument();
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("ManifestGroupQueries", "groups") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No groups.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("ManifestGroupQueries", "groups") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}
const badge = { selector: "span" } as const;

// Enable selected: the disabled beta group, selected, flips to Enabled through
// setManifestGroupsEnabled; the selection clears.
export const EnableSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    await waitFor(() => expect(table().getByText("beta-group")).toBeInTheDocument());
    expect(table().getAllByText("Enabled", badge)).toHaveLength(1);
    await userEvent.click(c.getByLabelText("Select #2"));
    await waitFor(() => expect(c.getByText(/1 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Enable selected"));
    await waitFor(() => expect(table().getAllByText("Enabled", badge)).toHaveLength(2));
    expect(c.queryByText(/1 selected/)).not.toBeInTheDocument();
  },
};

// Disable selected through the same call.
export const DisableSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    await waitFor(() => expect(table().getByText("alpha-group")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select all"));
    await waitFor(() => expect(c.getByText(/2 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Disable selected"));
    await waitFor(() => expect(table().queryAllByText("Enabled", badge)).toHaveLength(0));
  },
};

// Enable all / Disable all act on every group through setAllManifestGroupsEnabled; each is
// disabled while it would change nothing on the page.
export const EnableAllAndDisableAll: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(table().getByText("beta-group")).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Enable all" }));
      await waitFor(() => expect(table().getAllByText("Enabled", badge)).toHaveLength(2));
      expect(c.getByRole("button", { name: "Enable all" })).toBeDisabled();
      await userEvent.click(c.getByRole("button", { name: "Disable all" }));
      await waitFor(() => expect(table().getAllByText("Disabled", badge)).toHaveLength(2));
      expect(c.getByRole("button", { name: "Disable all" })).toBeDisabled();
    } finally {
      restore();
    }
  },
};

// Declining the "all" confirmation changes nothing.
export const DisableAllDeclined: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = () => within(c.getByRole("table"));
    const original = window.confirm;
    window.confirm = () => false;
    try {
      await waitFor(() => expect(table().getByText("alpha-group")).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Disable all" }));
      expect(table().getAllByText("Enabled", badge)).toHaveLength(1);
    } finally {
      window.confirm = original;
    }
  },
};

// The global cross-group dependency graph draws every group.
export const DependencyGraph: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("Dependency graph")).toBeInTheDocument();
    expect(c.getAllByText("alpha-group").length).toBeGreaterThan(1); // grid + graph node
  },
};

// Trigger Selected sends the selected groups to triggerGroups in one call; its count is of
// manifests. A group triggered again finds its manifests already queued.
export const TriggerSelectedGroups: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #1")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #1"));
      await userEvent.click(c.getByLabelText("Select #2"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(await c.findByText("4 queued across 2 of 2 manifest group(s).")).toBeInTheDocument();
      await waitFor(() => expect(c.queryByText(/selected$/)).not.toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #1"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(
        await c.findByText("0 queued, 2 already queued (that entry now runs as the trigger) across 1 of 1 manifest group(s)."),
      ).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A group deleted since it was listed is skipped with a note.
export const TriggerSelectedGroupsNotes: Story = {
  parameters: { mock: groupScenarioWithRetired },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #1000002")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #1000002"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(await c.findByRole("alert")).toHaveTextContent("Manifest group 1000002 not found.");
      expect(c.getByText("0 queued, 1 not found across 0 of 1 manifest group(s).")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Cancel Running asks first, then sends the selection to cancelGroups in one call.
export const CancelRunning: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByLabelText("Select #2")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #2"));
      await userEvent.click(c.getByRole("button", { name: "Cancel running" }));
      expect(
        await c.findByText("Cancellation requested for 1 execution(s) across 1 of 1 manifest group(s)."),
      ).toBeInTheDocument();
      await waitFor(() => expect(c.queryByText(/selected$/)).not.toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// Declining the confirmation cancels nothing.
export const CancelRunningDeclined: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const original = window.confirm;
    window.confirm = () => false;
    try {
      await waitFor(() => expect(c.getByLabelText("Select #2")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #2"));
      await userEvent.click(c.getByRole("button", { name: "Cancel running" }));
      expect(c.getByText("1 selected")).toBeInTheDocument();
      expect(c.queryByText(/Cancellation requested/)).not.toBeInTheDocument();
    } finally {
      window.confirm = original;
    }
  },
};
