import type { Meta, StoryObj } from "@storybook/react-vite";
import { ExecutionChildren } from "./ExecutionChildren";

const meta = {
  title: "Components/ExecutionChildren",
  component: ExecutionChildren,
  // executionChildren for parentId 1 is captured in the fixtures.
  parameters: { fixtures: true },
  args: { parentId: 1, childCount: 30_000 },
} satisfies Meta<typeof ExecutionChildren>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithChildren: Story = {};
