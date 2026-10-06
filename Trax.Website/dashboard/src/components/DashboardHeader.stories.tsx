import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { DashboardHeader } from "./DashboardHeader";
import { noteQueryResult } from "../lib/refreshStatus";
import { setPollSeconds } from "../lib/poll";

const env = (name: string) => ({
  resolvers: () => ({ ConfigQueries: { environmentName: () => name } }),
});

const meta = {
  title: "Components/DashboardHeader",
  component: DashboardHeader,
  parameters: { mock: env("Development") },
} satisfies Meta<typeof DashboardHeader>;

export default meta;
type Story = StoryObj<typeof meta>;

// The environment badge is blue for Development, as in the Blazor header; the UTC clock runs.
export const Development: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const badge = await c.findByTestId("environment-badge");
    expect(badge).toHaveTextContent("Environment: Development");
    expect(badge).toHaveStyle({ backgroundColor: "#1976D2" });
    expect(c.getByTestId("utc-clock")).toHaveTextContent(/\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC/);
    expect(c.getByText("Auto-refresh off")).toBeInTheDocument();
  },
};

export const Production: Story = {
  parameters: { mock: env("Production") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByTestId("environment-badge")).toHaveStyle({ backgroundColor: "#D32F2F" });
  },
};

export const OtherEnvironment: Story = {
  parameters: { mock: env("Staging") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByTestId("environment-badge")).toHaveStyle({ backgroundColor: "#757575" });
  },
};

// A failed query marks "Last refresh failed" (its message on hover) until one succeeds.
export const LastRefreshFailed: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByTestId("environment-badge");
    noteQueryResult("Network error: fetch failed");
    const mark = await c.findByText(/Last refresh failed/);
    expect(mark).toHaveAttribute("title", "Network error: fetch failed");
    noteQueryResult(null);
    await waitFor(() => expect(c.queryByText(/Last refresh failed/)).not.toBeInTheDocument());
  },
};

// With an auto-refresh interval set, the header counts down to the next refresh.
export const RefreshCountdown: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    setPollSeconds(30);
    const countdown = await c.findByLabelText("Next refresh");
    expect(countdown).toHaveTextContent(/\d+s/);
    setPollSeconds(0);
    await waitFor(() => expect(c.getByText("Auto-refresh off")).toBeInTheDocument());
  },
};
