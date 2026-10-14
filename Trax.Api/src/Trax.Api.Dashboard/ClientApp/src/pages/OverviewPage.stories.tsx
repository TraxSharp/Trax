import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { OverviewPage } from "./OverviewPage";
import { setOverviewPanel } from "../lib/overviewPanels";

const meta = {
  title: "Pages/Overview",
  component: OverviewPage,
  parameters: { route: "/" },
} satisfies Meta<typeof OverviewPage>;

export default meta;
type Story = StoryObj<typeof meta>;

// Offline, backed by the captured real-data fixtures.
export const Mock: Story = { parameters: { fixtures: true } };

// The same page against the live devhost on :5310 (requires it running).
export const Real: Story = { parameters: { real: true } };

// The time-range toggle re-titles the executions chart and re-queries; the server panel shows CPU.
export const RangeToggleAndCpu: Story = {
  parameters: { fixtures: true },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("heading", { name: "Overview" })).toBeInTheDocument(),
    );
    expect(c.getByRole("button", { name: "24h" })).toHaveAttribute("aria-pressed", "true");
    expect(c.getByText(/Executions over time \(last 24h\)/)).toBeInTheDocument();
    expect(c.getByText("CPU")).toBeInTheDocument();

    await userEvent.click(c.getByRole("button", { name: "1h" }));
    await waitFor(() =>
      expect(c.getByText(/Executions over time \(last hour\)/)).toBeInTheDocument(),
    );
    expect(c.getByRole("button", { name: "1h" })).toHaveAttribute("aria-pressed", "true");
  },
};

// Hiding panels in User settings removes them from the Overview at once.
export const HiddenPanels: Story = {
  parameters: { fixtures: true },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Overview" })).toBeInTheDocument());
    expect(c.getByText("Executions today")).toBeInTheDocument();
    expect(c.getByText("Top failures (7d)")).toBeInTheDocument();
    setOverviewPanel("summaryCards", false);
    setOverviewPanel("failures", false);
    setOverviewPanel("serverHealth", false);
    await waitFor(() => expect(c.queryByText("Executions today")).not.toBeInTheDocument());
    expect(c.queryByText("Top failures (7d)")).not.toBeInTheDocument();
    expect(c.queryByText("CPU")).not.toBeInTheDocument();
    expect(c.getByText("Slowest trains (7d)")).toBeInTheDocument();
    expect(c.getByText(/Executions over time/)).toBeInTheDocument();
  },
};
