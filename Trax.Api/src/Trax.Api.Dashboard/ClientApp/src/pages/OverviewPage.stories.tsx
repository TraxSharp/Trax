import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { OverviewPage } from "./OverviewPage";
import { setOverviewPanel } from "../lib/overviewPanels";
import { attentionAllClear, attentionNeedsWork, attentionScenario, overviewScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Overview",
  component: OverviewPage,
  parameters: { route: "/" },
} satisfies Meta<typeof OverviewPage>;

export default meta;
type Story = StoryObj<typeof meta>;

// Offline, backed by the captured real-data fixtures, with something needing attention.
export const Mock: Story = { parameters: { fixtures: true, mock: attentionScenario } };

// The same page against the live devhost on :5310 (requires it running).
export const Real: Story = { parameters: { real: true } };

// The page leads with what needs attention, each entry a link to the list it counts.
export const NeedsAttention: Story = {
  parameters: { mock: overviewScenario(attentionNeedsWork, { runs: true }) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const attention = await c.findByRole("region", { name: "Needs attention" });
    const a = within(attention);
    await waitFor(() =>
      expect(a.getByRole("link", { name: /Job dispatcher paused/ })).toHaveAttribute("href", "/system/scheduler"),
    );
    expect(a.getByRole("link", { name: /2 dead letters of IChargeCardTrain/ }).getAttribute("href")).toMatch(
      /^\/runs\/dead-letters\?status=AWAITING_INTERVENTION&train=Trax\.Samples\.Billing\.IChargeCardTrain&reason=/,
    );
    expect(a.getByRole("link", { name: /IChargeCardTrain failing more than usual/ })).toHaveAttribute(
      "href",
      "/runs?state=FAILED&train=Trax.Samples.Billing.IChargeCardTrain",
    );
    expect(a.getByRole("link", { name: /3 stuck order instances in AwaitingPayment/ })).toHaveAttribute(
      "href",
      "/machines/order?state=AwaitingPayment&stuck=true",
    );
    expect(a.getByRole("link", { name: /42 queued runs waiting/ })).toHaveAttribute("href", "/runs/queued?status=QUEUED");
    expect(a.getByRole("link", { name: /Host trax-worker-2 offline/ })).toHaveAttribute("href", "/system/hosts");
    expect(c.queryByRole("region", { name: "No runs in the last 7 days" })).not.toBeInTheDocument();
  },
};

// Nothing needs attention: the page says so instead of an empty list.
export const AllClear: Story = {
  parameters: { mock: overviewScenario(attentionAllClear, { runs: true }) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Nothing needs attention/)).toBeInTheDocument();
  },
};

// A host where nothing has run: say what to do, and link the Trains page and the getting-started guide.
export const FirstRun: Story = {
  parameters: { mock: overviewScenario(attentionAllClear, { runs: false }) },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const firstRun = await c.findByRole("region", { name: "No runs in the last 7 days" });
    const f = within(firstRun);
    expect(f.getByRole("link", { name: "Trains page" })).toHaveAttribute("href", "/trains");
    expect(f.getByRole("link", { name: "getting-started guide" })).toHaveAttribute(
      "href",
      "https://traxsharp.net/docs/getting-started",
    );
    expect(c.queryByText(/Runs over time/)).not.toBeInTheDocument();
  },
};

// The time-range toggle re-titles the runs chart and re-queries. The dashboard host's own process
// stats are not on the Overview.
export const RangeToggle: Story = {
  parameters: { fixtures: true, mock: attentionScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("heading", { name: "Overview" })).toBeInTheDocument(),
    );
    expect(c.getByRole("button", { name: "24h" })).toHaveAttribute("aria-pressed", "true");
    expect(c.getByText(/Runs over time \(last 24h\)/)).toBeInTheDocument();
    expect(c.queryByText("CPU")).not.toBeInTheDocument();
    expect(c.queryByText("GC heap")).not.toBeInTheDocument();

    await userEvent.click(c.getByRole("button", { name: "1h" }));
    await waitFor(() =>
      expect(c.getByText(/Runs over time \(last hour\)/)).toBeInTheDocument(),
    );
    expect(c.getByRole("button", { name: "1h" })).toHaveAttribute("aria-pressed", "true");
  },
};

// Hiding panels in User settings removes them from the Overview at once.
export const HiddenPanels: Story = {
  parameters: { fixtures: true, mock: attentionScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Overview" })).toBeInTheDocument());
    expect(c.getByText("Runs today")).toBeInTheDocument();
    expect(c.getByText("Top failures (7d)")).toBeInTheDocument();
    setOverviewPanel("summaryCards", false);
    setOverviewPanel("failures", false);
    await waitFor(() => expect(c.queryByText("Runs today")).not.toBeInTheDocument());
    expect(c.queryByText("Top failures (7d)")).not.toBeInTheDocument();
    expect(c.getByText("Slowest trains (7d)")).toBeInTheDocument();
    expect(c.getByText(/Runs over time/)).toBeInTheDocument();
  },
};
