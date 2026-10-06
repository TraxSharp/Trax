import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, within } from "storybook/test";
import { Modal } from "./Modal";

const meta = {
  title: "Components/Modal",
  component: Modal,
  args: {
    title: "Confirm action",
    onClose: () => {},
    children: (
      <div className="text-sm text-fg-2">
        Modal body content goes here. Click the backdrop or press Escape to close.
      </div>
    ),
  },
} satisfies Meta<typeof Modal>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};

// Pressing Escape closes the modal (window keydown handler).
export const ClosesOnEscape: Story = {
  args: { onClose: fn() },
  play: async ({ args }) => {
    await userEvent.keyboard("{Escape}");
    expect(args.onClose).toHaveBeenCalled();
  },
};

// Clicking the backdrop closes the modal.
export const ClosesOnBackdrop: Story = {
  args: { onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const backdrop = canvasElement.querySelector<HTMLElement>(".fixed.inset-0")!;
    await userEvent.click(backdrop);
    expect(args.onClose).toHaveBeenCalled();
  },
};

// Clicking inside the card does NOT close (stopPropagation on the panel).
export const BodyClickKeepsOpen: Story = {
  args: { onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByText(/Modal body content/));
    expect(args.onClose).not.toHaveBeenCalled();
  },
};
