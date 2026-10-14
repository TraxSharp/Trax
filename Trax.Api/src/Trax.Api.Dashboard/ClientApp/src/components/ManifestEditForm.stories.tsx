import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, waitFor, within } from "storybook/test";
import { ManifestEditForm } from "./ManifestEditForm";
import type { ManifestSummary } from "../types";

const manifest: ManifestSummary = {
  id: 42,
  externalId: "man-00000000000000000000000000000042",
  name: "Trax.Demo.Trains.OrderTrain",
  isEnabled: true,
  scheduleType: "CRON",
  cronExpression: "0 */5 * * * *",
  intervalSeconds: null,
  maxRetries: 3,
  timeoutSeconds: 120,
  lastSuccessfulRun: "2026-07-07T11:00:00.000Z",
  manifestGroupId: 1,
  dependsOnManifestId: null,
  priority: 0,
};

const meta = {
  title: "Components/ManifestEditForm",
  component: ManifestEditForm,
  args: { manifest, onSaved: () => {}, onClose: () => {} },
} satisfies Meta<typeof ManifestEditForm>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Cron: Story = {};
export const Interval: Story = {
  args: {
    manifest: { ...manifest, scheduleType: "INTERVAL", cronExpression: null, intervalSeconds: 60 },
  },
};

// Switching the schedule type swaps the conditional cron/interval fields.
export const ScheduleTypeToggle: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText("Cron expression")).toBeInTheDocument();
    await userEvent.selectOptions(c.getByRole("combobox"), "INTERVAL");
    await waitFor(() => expect(c.queryByText("Cron expression")).not.toBeInTheDocument());
    expect(c.getByText("Interval (seconds)")).toBeInTheDocument();
    await userEvent.selectOptions(c.getByRole("combobox"), "NONE");
    await waitFor(() => expect(c.queryByText("Interval (seconds)")).not.toBeInTheDocument());
  },
};

// Full save round-trip through the UpdateManifest overlay: clearing the timeout field sends
// clearTimeout, and on the success ACK the form calls onSaved then onClose.
export const SaveClearsTimeout: Story = {
  args: { onSaved: fn(), onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const timeout = c.getByLabelText("Timeout (seconds, blank = none)");
    expect(timeout).toHaveValue(120);
    await userEvent.clear(timeout);
    expect(timeout).toHaveValue(null); // blank number input => clearTimeout in the mutation input

    const retries = c.getByLabelText("Max retries");
    await userEvent.clear(retries);
    await userEvent.type(retries, "7");

    await userEvent.click(c.getByRole("button", { name: /save changes/i }));
    await waitFor(() => expect(args.onSaved).toHaveBeenCalled());
    expect(args.onClose).toHaveBeenCalled();
  },
};

// Switching to CRON then saving a new expression persists it through the overlay (onSaved fires).
export const SaveCronChange: Story = {
  args: {
    manifest: { ...manifest, scheduleType: "INTERVAL", cronExpression: null, intervalSeconds: 30 },
    onSaved: fn(),
  },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.selectOptions(c.getByRole("combobox"), "CRON");
    const cron = await c.findByPlaceholderText("0 */5 * * * *");
    await userEvent.clear(cron);
    await userEvent.type(cron, "0 0 * * * *");
    await userEvent.click(c.getByRole("button", { name: /save changes/i }));
    await waitFor(() => expect(args.onSaved).toHaveBeenCalled());
  },
};

// Cancel closes the form without saving.
export const CancelWithoutSaving: Story = {
  args: { onSaved: fn(), onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByRole("button", { name: "Cancel" }));
    expect(args.onClose).toHaveBeenCalled();
    expect(args.onSaved).not.toHaveBeenCalled();
  },
};
