import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ManifestDetailPage } from "./ManifestDetailPage";
import { manifestDetailScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Manifest detail",
  component: ManifestDetailPage,
  parameters: {
    routePath: "/manifests/:id",
    route: "/manifests/801",
    mock: manifestDetailScenario,
  },
} satisfies Meta<typeof ManifestDetailPage>;

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

export const Real: Story = { parameters: { real: true, route: "/manifests/5000" } };

// Disable an enabled manifest -> badge flips to Disabled.
export const EnableDisable: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    expect(c.getByText("Enabled")).toBeInTheDocument();
    await userEvent.click(c.getByText("Disable"));
    await waitFor(() => expect(c.getByText("Disabled")).toBeInTheDocument());
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
    // Recent executions (manifest-scoped history).
    await waitFor(() => expect(c.getByText("Recent executions")).toBeInTheDocument());
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
      await userEvent.click(c.getByText("Trigger"));
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
    expect(c.getByRole("link", { name: "alpha-group" })).toHaveAttribute("href", "/groups/1");
    expect(c.getByText("Properties (OrderInput)")).toBeInTheDocument();
    expect(c.getByText(/"apiKey": "\[REDACTED\]"/)).toBeInTheDocument();
  },
};

// A dependent manifest links the manifest it depends on.
export const DependsOnLink: Story = {
  parameters: { route: "/manifests/803" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "ReportManifest" })).toBeInTheDocument());
    expect(c.getByRole("link", { name: "#801" })).toHaveAttribute("href", "/manifests/801");
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
      await userEvent.click(c.getByText("Trigger, ask afresh"));
      expect(await c.findByText(/a released retry asks afresh/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// A manifest the server does not have shows the not-found state.
export const NotFound: Story = {
  parameters: {
    route: "/manifests/9999",
    mock: { resolvers: () => ({ OperationsQueries: { manifestDetail: () => null } }) },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No manifest found.")).toBeInTheDocument();
  },
};
