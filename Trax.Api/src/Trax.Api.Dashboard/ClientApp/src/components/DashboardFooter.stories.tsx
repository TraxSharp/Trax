import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, within } from "storybook/test";
import { DashboardFooter } from "./DashboardFooter";
import { errorOverride } from "../mock/scenarios";

const meta = {
  title: "Components/Dashboard footer",
  component: DashboardFooter,
  parameters: {
    mock: { resolvers: () => ({ ConfigQueries: { version: () => "1.46.0" } }) },
  },
} satisfies Meta<typeof DashboardFooter>;

export default meta;
type Story = StoryObj<typeof meta>;

// The footer names the Trax version the API host runs.
export const ShowsVersion: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await expect(await c.findByText("Trax Dashboard — v1.46.0")).toBeInTheDocument();
  },
};

// A failed read shows nothing rather than a wrong version.
export const VersionUnavailable: Story = {
  parameters: { mock: errorOverride("ConfigQueries", "version") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await new Promise((r) => setTimeout(r, 50));
    expect(c.queryByText(/Trax Dashboard — v/)).not.toBeInTheDocument();
  },
};
