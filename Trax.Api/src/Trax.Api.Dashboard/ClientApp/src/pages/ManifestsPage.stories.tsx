import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ManifestsPage } from "./ManifestsPage";
import { emptyPage, errorOverride, manifestScenario, manifestScenarioWithRetired } from "../mock/scenarios";
import type { StatefulOverlay } from "../mock/store/overlays";

const meta = {
  title: "Pages/Manifests",
  component: ManifestsPage,
  parameters: { route: "/manifests", mock: manifestScenario },
} satisfies Meta<typeof ManifestsPage>;

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

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// Disable an enabled manifest -> its badge flips to Disabled (read-after-write, keyed by externalId).
export const EnableDisable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    expect(c.getAllByText("Enabled", badge)).toHaveLength(2); // 801, 802
    const row = c.getByText("OrderManifest").closest("tr") as HTMLElement;
    await userEvent.click(within(row).getByText("Disable"));
    await waitFor(() => expect(c.getAllByText("Enabled", badge)).toHaveLength(1));
  },
};

// Trigger a manifest: confirm, run, no error (the list is unchanged).
export const TriggerManifest: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
      const row = c.getByText("OrderManifest").closest("tr") as HTMLElement;
      await userEvent.click(within(row).getByText("Trigger"));
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// The enabled filter (2nd select) narrows to disabled manifests.
export const FilterByEnabled: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "false");
    await waitFor(() => expect(c.queryByText("OrderManifest")).not.toBeInTheDocument());
    expect(c.getByText("ReportManifest")).toBeInTheDocument();
  },
};

// The schedule filter (1st select) narrows by schedule type.
export const FilterBySchedule: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "INTERVAL");
    await waitFor(() => expect(c.queryByText("OrderManifest")).not.toBeInTheDocument());
    expect(c.getByText("EmailManifest")).toBeInTheDocument();
  },
};

// Schedule + enabled filters compose: disabled + CRON leaves only ArchiveManifest (#804).
export const FilterByScheduleAndEnabled: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "CRON");
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "false");
    await waitFor(() => expect(c.queryByText("OrderManifest")).not.toBeInTheDocument());
    expect(c.getByText("ArchiveManifest")).toBeInTheDocument();
    expect(c.queryByText("ReportManifest")).not.toBeInTheDocument(); // disabled but NONE schedule
  },
};

// Name + enabled filters compose: "Order" + enabled leaves only OrderManifest (#801).
export const FilterByNameAndEnabled: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by name…"), "Order");
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "true");
    await waitFor(() => expect(c.queryByText("EmailManifest")).not.toBeInTheDocument());
    expect(c.getByText("OrderManifest")).toBeInTheDocument();
  },
};

// Every disabled row shows Enable and none shows Disable (and vice-versa).
export const DisabledManifestsShowEnable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "false");
    await waitFor(() => expect(c.queryByText("OrderManifest")).not.toBeInTheDocument());
    expect(c.getAllByRole("button", { name: "Enable" })).toHaveLength(2); // 803, 804
    expect(c.queryByRole("button", { name: "Disable" })).not.toBeInTheDocument();
  },
};

// A filter combination that matches nothing shows the empty state: the only NONE-scheduled
// manifest is disabled, so enabled + NONE is empty.
export const FilterCombinationYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.selectOptions(c.getAllByRole("combobox")[0], "NONE");
    await userEvent.selectOptions(c.getAllByRole("combobox")[1], "true");
    await waitFor(() => expect(c.getByText("No manifests.")).toBeInTheDocument());
  },
};

// Select two enabled manifests and bulk-disable them in one setManifestsEnabled call.
export const BulkDisableSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    expect(c.getAllByText("Enabled", badge)).toHaveLength(2); // 801, 802
    await userEvent.click(c.getByLabelText("Select #801"));
    await userEvent.click(c.getByLabelText("Select #802"));
    await waitFor(() => expect(c.getByText(/2 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Disable selected"));
    await waitFor(() => expect(c.queryAllByText("Enabled", badge)).toHaveLength(0));
  },
};

// Select-all checks every row; Clear resets it.
export const SelectAllAndClear: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByLabelText("Select #801")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select all"));
    await waitFor(() => expect(c.getByText(/4 selected/)).toBeInTheDocument());
    await userEvent.click(c.getByText("Clear"));
    await waitFor(() => expect(c.queryByText(/selected/)).not.toBeInTheDocument());
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("OperationsQueries", "manifests") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No manifests.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("OperationsQueries", "manifests") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

// Bulk enable goes to setManifestsEnabled in one call: both disabled rows flip to Enabled.
export const BulkEnableSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select #803"));
    await userEvent.click(c.getByLabelText("Select #804"));
    await userEvent.click(c.getByText("Enable selected"));
    await waitFor(() => expect(c.getAllByText("Enabled", badge)).toHaveLength(4));
    expect(await c.findByText(/2 manifest\(s\) enabled/)).toBeInTheDocument();
    expect(c.queryByText(/selected$/)).not.toBeInTheDocument();
  },
};

// The Retries column reads each manifest's replay-on-retry flag; 803 asks afresh.
export const RetriesColumn: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("ReportManifest")).toBeInTheDocument());
    const report = c.getByText("ReportManifest").closest("tr") as HTMLElement;
    expect(within(report).getByText("Ask afresh")).toBeInTheDocument();
    expect(c.getAllByText("Replay")).toHaveLength(3);
  },
};

// "Ask afresh on retry" and "Replay on retry" set the flag for the selection in one call.
export const BulkReplayOnRetry: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    await userEvent.click(c.getByLabelText("Select #801"));
    await userEvent.click(c.getByLabelText("Select #802"));
    await userEvent.click(c.getByText("Ask afresh on retry"));
    await waitFor(() => expect(c.getAllByText("Ask afresh")).toHaveLength(3));
    await userEvent.click(c.getByLabelText("Select #803"));
    await userEvent.click(c.getByText("Replay on retry"));
    await waitFor(() => expect(c.getAllByText("Ask afresh")).toHaveLength(2));
  },
};

// "Hide admin trains" (on by default, shared with the Executions grid) leaves out the manifests of
// the scheduler's own trains, server side; turning it off lists them.
export const HideAdminTrains: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
    const toggle = c.getByRole("checkbox", { name: "Hide admin trains" });
    expect(toggle).toBeChecked();
    expect(c.queryByText("IManifestManagerTrain")).not.toBeInTheDocument();
    await userEvent.click(toggle);
    expect(await c.findByText("IManifestManagerTrain")).toBeInTheDocument();
    await userEvent.click(toggle);
    await waitFor(() => expect(c.queryByText("IManifestManagerTrain")).not.toBeInTheDocument());
  },
};

// Trigger Selected sends the selection to triggerManifests in one call and reports its count; the
// selection clears. Triggering the same manifest again finds its entry already queued.
export const TriggerSelected: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #801"));
      await userEvent.click(c.getByLabelText("Select #802"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(await c.findByText("2 queued across 2 of 2 manifest(s).")).toBeInTheDocument();
      await waitFor(() => expect(c.queryByText(/selected$/)).not.toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #801"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(
        await c.findByText("0 queued, 1 already queued (that entry now runs as the trigger) across 1 of 1 manifest(s)."),
      ).toBeInTheDocument();
      expect(c.queryByRole("alert")).not.toBeInTheDocument(); // nothing noted
    } finally {
      restore();
    }
  },
};

// The ask-afresh variant sends askAfresh: true, and the count says so.
export const TriggerSelectedAskAfresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #803"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected, ask afresh" }));
      expect(await c.findByText("1 queued across 1 of 1 manifest(s), asking afresh.")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Declining the confirmation triggers nothing and keeps the selection.
export const TriggerSelectedDeclined: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const original = window.confirm;
    window.confirm = () => false;
    try {
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #801"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(c.getByText("1 selected")).toBeInTheDocument();
      expect(c.queryByText(/queued across/)).not.toBeInTheDocument();
    } finally {
      window.confirm = original;
    }
  },
};

// A manifest deleted since the page listed it is skipped with a note, shown under the count.
export const TriggerSelectedNotes: Story = {
  parameters: { mock: manifestScenarioWithRetired },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("RetiredManifest")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #801"));
      await userEvent.click(c.getByLabelText("Select #1000001"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(await c.findByRole("alert")).toHaveTextContent("Manifest 1000001 not found.");
      expect(c.getByText("1 queued, 1 not found across 1 of 2 manifest(s).")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A batch the API refuses (here, too many ids) is shown as an error and keeps the selection, so it
// can be changed and sent again.
const refusingTrigger: StatefulOverlay = {
  mutations: {
    TriggerManifests: () => ({
      operations: {
        triggerManifests: {
          success: false,
          matched: 0,
          queued: 0,
          alreadyQueued: 0,
          tooLateToAskAfresh: 0,
          skipped: 0,
          message: "At most 1000 ids can be sent at once.",
          notes: [],
        },
      },
    }),
  },
};

export const TriggerSelectedRefused: Story = {
  parameters: { overlays: [refusingTrigger] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument());
      await userEvent.click(c.getByLabelText("Select #801"));
      await userEvent.click(c.getByRole("button", { name: "Trigger selected" }));
      expect(await c.findByRole("alert")).toHaveTextContent("At most 1000 ids can be sent at once.");
      expect(c.getByText("1 selected")).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};
