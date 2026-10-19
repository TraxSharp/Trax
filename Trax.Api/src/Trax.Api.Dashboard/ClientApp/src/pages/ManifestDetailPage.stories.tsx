import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ManifestDetailPage } from "./ManifestDetailPage";
import { manifestDetailScenario } from "../mock/scenarios";
import { setAutoConfirm } from "../lib/confirm";

const meta = {
  title: "Pages/Manifest detail",
  component: ManifestDetailPage,
  parameters: {
    routePath: "/schedules/:id",
    route: "/schedules/801",
    mock: manifestDetailScenario,
  },
} satisfies Meta<typeof ManifestDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  setAutoConfirm(true);
  return () => {
    setAutoConfirm(null);
  };
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true, route: "/schedules/5000" } };

// The switch beside the heading disables an enabled manifest after the console asks, and the
// badge flips to Disabled; it enables again without asking.
export const EnableDisable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
      expect(c.getByText("Enabled")).toBeInTheDocument();
      await userEvent.click(c.getByRole("switch", { name: "Manifest enabled" }));
      await waitFor(() => expect(c.getByText("Disabled")).toBeInTheDocument());
      expect(c.getByRole("switch", { name: "Manifest enabled" })).not.toBeChecked();
      await userEvent.click(c.getByRole("switch", { name: "Manifest enabled" }));
      await waitFor(() => expect(c.getByText("Enabled")).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// Declining the question leaves the manifest enabled.
export const DisableDeclined: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    setAutoConfirm(false);
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("switch", { name: "Manifest enabled" }));
      expect(c.getByText("Enabled")).toBeInTheDocument();
      expect(c.getByRole("switch", { name: "Manifest enabled" })).toBeChecked();
    } finally {
      setAutoConfirm(null);
    }
  },
};

// When it runs: the schedule in words, the next runs, and the run history by window.
export const ScheduleAndHistory: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const schedule = await c.findByRole("region", { name: "Schedule" });
    expect(within(schedule).getByText("Every 5 minutes")).toBeInTheDocument();
    expect(within(schedule).getAllByRole("listitem")).toHaveLength(5);
    const history = await c.findByRole("region", { name: "Run history" });
    await waitFor(() => expect(within(history).getByText(/completed/)).toBeInTheDocument());
    await userEvent.click(within(history).getByRole("button", { name: "7 days" }));
    await waitFor(() =>
      expect(within(history).getByRole("button", { name: "7 days" })).toHaveAttribute("aria-pressed", "true"),
    );
  },
};

// Edit opens the manifest edit form; Cancel closes it.
export const EditForm: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    await userEvent.click(c.getByText("Edit"));
    await waitFor(() => expect(c.getByText("Edit manifest")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(c.queryByText("Edit manifest")).not.toBeInTheDocument());
  },
};

// The detail page renders stat cards, the exclusion windows, and the recent-executions list.
export const Richness: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument(),
    );
    // Stat cards (loaded by a separate manifestStats query): total 42, completed 30.
    await waitFor(() => expect(c.getByText("Total runs")).toBeInTheDocument());
    expect(c.getByText("42")).toBeInTheDocument();
    expect(c.getByText("30")).toBeInTheDocument();
    // Exclusion windows (typed per kind).
    await waitFor(() => expect(c.getByText("Exclusion windows")).toBeInTheDocument());
    expect(c.getByText(/Skips Saturday, Sunday/)).toBeInTheDocument();
    expect(c.getByText(/Skips daily 02:00:00 to 04:00:00/)).toBeInTheDocument();
    // Recent runs (manifest-scoped history).
    await waitFor(() => expect(c.getByText("Recent runs")).toBeInTheDocument());
    await waitFor(() => expect(c.getByText("#950")).toBeInTheDocument());
  },
};

// Trigger confirms and runs without error.
export const Trigger: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
      await userEvent.click(c.getByText("Queue now"));
      await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// manifestDetail fields: the group by name, the schedule details, and the properties with the
// sensitive value masked as the API masks it.
export const DetailFields: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    expect(c.getByText("Misfire policy").nextElementSibling).toHaveTextContent("Fire once now");
    expect(c.getByText("Misfire threshold (s)").nextElementSibling).toHaveTextContent("60");
    expect(c.getByText("Variance (s)").nextElementSibling).toHaveTextContent("15");
    expect(c.getByText("Next scheduled run").nextElementSibling).not.toHaveTextContent("—");
    expect(c.getByRole("link", { name: "alpha-group" })).toHaveAttribute("href", "/schedules/groups/1");
    expect(c.getByText("Properties (OrderInput)")).toBeInTheDocument();
    expect(c.getByText(/"apiKey": "\[REDACTED\]"/)).toBeInTheDocument();
  },
};

// A dependent manifest links the manifest it depends on.
export const DependsOnLink: Story = {
  parameters: { route: "/schedules/803" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "ReportManifest" })).toBeInTheDocument());
    expect(c.getByRole("link", { name: "#801" })).toHaveAttribute("href", "/schedules/801");
    expect(c.queryByText(/Properties/)).not.toBeInTheDocument(); // no properties
  },
};

// The replay-on-retry switch writes setManifestsReplayDecisionsOnRetry and reads back.
export const ReplayOnRetrySwitch: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    const toggle = c.getByRole("switch", { name: "Replay decisions on retry" });
    expect(toggle).toBeChecked();
    expect(c.getByText("Yes")).toBeInTheDocument();
    await userEvent.click(toggle);
    await waitFor(() => expect(c.getByText("No (retries ask afresh)")).toBeInTheDocument());
    expect(await c.findByText(/ask afresh\./)).toBeInTheDocument(); // the toast
    await userEvent.click(c.getByRole("switch", { name: "Replay decisions on retry" }));
    await waitFor(() => expect(c.getByRole("switch", { name: "Replay decisions on retry" })).toBeChecked());
  },
};

// Trigger, ask afresh: confirms and runs, and says a released retry asks afresh.
export const TriggerAskAfresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
      await userEvent.click(c.getByText("Queue now, ask afresh"));
      expect(await c.findByText(/a released retry asks afresh/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A manifest the server does not have shows the not-found state.
export const NotFound: Story = {
  parameters: {
    route: "/schedules/9999",
    mock: { resolvers: () => ({ OperationsQueries: { manifestDetail: () => null } }) },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No manifest found.")).toBeInTheDocument();
  },
};
