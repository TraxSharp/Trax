import type { Meta, StoryObj } from "@storybook/react-vite";
import { ConnectionIndicator } from "./ConnectionIndicator";

const meta = {
  title: "Components/ConnectionIndicator",
  component: ConnectionIndicator,
} satisfies Meta<typeof ConnectionIndicator>;

export default meta;
type Story = StoryObj<typeof meta>;

// Reflects the live subscription status; shows "closed" with no socket connected.
export const Default: Story = {};
