import { useEffect } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ToastHost } from "./ToastHost";
import { toast } from "../lib/toast";

// The decorator already mounts a ToastHost, so this story just fires a few toasts to show the
// three kinds. They stack bottom-right and auto-dismiss.
function TriggerToasts() {
  useEffect(() => {
    toast("Manifest updated.", "success");
    toast("Could not re-queue: unknown train.", "error");
    toast("Cancellation requested.", "info");
  }, []);
  return (
    <div className="text-sm text-muted">
      Toasts appear in the bottom-right corner.
    </div>
  );
}

const meta = {
  title: "Components/ToastHost",
  component: ToastHost,
} satisfies Meta<typeof ToastHost>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Toasts: Story = { render: () => <TriggerToasts /> };

// Clicking a toast's dismiss button removes it.
export const Dismiss: Story = {
  render: () => <TriggerToasts />,
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getAllByRole("status").length).toBeGreaterThan(0));
    const before = c.getAllByRole("status").length;
    await userEvent.click(c.getAllByLabelText("Dismiss")[0]);
    await waitFor(() => expect(c.getAllByRole("status").length).toBe(before - 1));
  },
};
