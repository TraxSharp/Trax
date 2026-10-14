import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, within } from "storybook/test";
import { StateBadge } from "./StateBadge";
import type { TrainState } from "../types";

const STATES: TrainState[] = ["PENDING", "IN_PROGRESS", "COMPLETED", "FAILED", "CANCELLED"];

const meta = {
  title: "Components/StateBadge",
  component: StateBadge,
  args: { state: "COMPLETED" },
} satisfies Meta<typeof StateBadge>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const AllStates: Story = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      {STATES.map((s) => (
        <StateBadge key={s} state={s} />
      ))}
    </div>
  ),
  // Every TrainState maps to its human label (IN_PROGRESS becomes "In progress", etc.).
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    for (const label of ["Pending", "In progress", "Completed", "Failed", "Cancelled"]) {
      expect(c.getByText(label)).toBeInTheDocument();
    }
  },
};
