import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, within } from "storybook/test";
import { StateTimeline } from "./StateTimeline";

const START = "2026-07-07T11:58:00.000Z";
const END = "2026-07-07T11:59:12.000Z"; // 72s later

const meta = {
  title: "Components/StateTimeline",
  component: StateTimeline,
  args: { state: "COMPLETED", startTime: START, endTime: END },
} satisfies Meta<typeof StateTimeline>;

export default meta;
type Story = StoryObj<typeof meta>;

// Completed: terminal step reads "Completed" and the runtime is shown.
export const Completed: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText("Completed")).toBeInTheDocument();
    expect(c.getByText("Ran for 72.0s")).toBeInTheDocument();
  },
};

// Failed: the terminal step relabels to "Failed" (no "Completed").
export const Failed: Story = {
  args: { state: "FAILED" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText("Failed")).toBeInTheDocument();
    expect(c.queryByText("Completed")).not.toBeInTheDocument();
  },
};

// In progress: no runtime line (no end time yet).
export const InProgress: Story = {
  args: { state: "IN_PROGRESS", endTime: null },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText("In progress")).toBeInTheDocument();
    expect(c.queryByText(/Ran for/)).not.toBeInTheDocument();
  },
};

// Pending: earliest step only, still no runtime.
export const Pending: Story = {
  args: { state: "PENDING", endTime: null },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText("Pending")).toBeInTheDocument();
    expect(c.queryByText(/Ran for/)).not.toBeInTheDocument();
  },
};
