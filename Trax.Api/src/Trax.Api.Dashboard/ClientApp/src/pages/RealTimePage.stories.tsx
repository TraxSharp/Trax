import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { RealTimePage } from "./RealTimePage";

const meta = {
  title: "Pages/Live events",
  component: RealTimePage,
  parameters: { route: "/system/live" },
} satisfies Meta<typeof RealTimePage>;

export default meta;
type Story = StoryObj<typeof meta>;

// With no simulator driving the mock subscriptions, the page renders its idle state: the domain
// counters at zero and the "waiting for events" empty feed.
export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Live events" })).toBeInTheDocument());
    expect(c.getByText(/Waiting for events/)).toBeInTheDocument();
    expect(c.getByText("Work queue")).toBeInTheDocument();
  },
};

export const Real: Story = { parameters: { real: true } };
