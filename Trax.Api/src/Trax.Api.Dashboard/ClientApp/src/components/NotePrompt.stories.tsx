import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, waitFor, within } from "storybook/test";
import { NotePrompt } from "./NotePrompt";

const meta = {
  title: "Components/NotePrompt",
  component: NotePrompt,
  args: {
    title: "Acknowledge dead letter #1000000",
    label: "Resolution note (why is this being closed without retry?)",
    confirmLabel: "Acknowledge",
    busy: false,
    onConfirm: () => {},
    onClose: () => {},
  },
} satisfies Meta<typeof NotePrompt>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};
export const Busy: Story = { args: { busy: true } };

// The confirm button stays disabled until the note has non-whitespace content, and the note
// passed to onConfirm is trimmed. Guards against acknowledging a dead letter with an empty reason.
export const RequiresNonBlankNote: Story = {
  args: { onConfirm: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const confirm = c.getByRole("button", { name: "Acknowledge" });
    const box = c.getByPlaceholderText("Reason / note…");

    // Empty: disabled.
    expect(confirm).toBeDisabled();

    // Whitespace only: trim() is empty, so still disabled.
    await userEvent.type(box, "    ");
    expect(confirm).toBeDisabled();

    // Real content (with surrounding whitespace) enables it; the submitted note is trimmed.
    await userEvent.type(box, "root cause: bad migration  ");
    await waitFor(() => expect(confirm).toBeEnabled());
    await userEvent.click(confirm);
    expect(args.onConfirm).toHaveBeenCalledWith("root cause: bad migration");

    // Clearing it back to empty re-disables.
    await userEvent.clear(box);
    await waitFor(() => expect(confirm).toBeDisabled());
  },
};

// Cancel closes without confirming, even after text was typed.
export const CancelDiscards: Story = {
  args: { onConfirm: fn(), onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.type(c.getByPlaceholderText("Reason / note…"), "changed my mind");
    await userEvent.click(c.getByRole("button", { name: "Cancel" }));
    expect(args.onClose).toHaveBeenCalled();
    expect(args.onConfirm).not.toHaveBeenCalled();
  },
};

// The scheduler refuses a note over 1,000 characters, so the box takes no more.
export const NoteCappedAt1000: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const box = c.getByRole("textbox");
    expect(box).toHaveAttribute("maxlength", "1000");
  },
};
