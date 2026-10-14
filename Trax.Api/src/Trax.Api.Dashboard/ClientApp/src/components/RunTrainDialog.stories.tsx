import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, waitFor, within } from "storybook/test";
import { RunTrainDialog } from "./RunTrainDialog";
import { trainsScenario } from "../mock/scenarios";

const meta = {
  title: "Components/RunTrainDialog",
  component: RunTrainDialog,
  parameters: { mock: trainsScenario },
  args: { onClose: () => {} },
} satisfies Meta<typeof RunTrainDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};

// Run is disabled until a train is chosen; a run closes the dialog.
export const RunsAndCloses: Story = {
  args: { onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const submit = c.getByRole("button", { name: "Run train" });
    expect(submit).toBeDisabled();
    const select = c.getByRole("combobox", { name: "Train" });
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBeGreaterThan(1));
    await userEvent.selectOptions(select, "Trax.Trains.ZebraTrain");
    await userEvent.type(c.getByLabelText("Field playerId"), "player-7");
    await userEvent.selectOptions(c.getByRole("combobox", { name: "Field tier" }), "Silver");
    await userEvent.click(submit);
    await waitFor(() => expect(args.onClose).toHaveBeenCalled());
  },
};

// A refusal keeps the dialog open with the server's reason.
export const Refusal: Story = {
  args: { onClose: fn(), initialTrain: "Trax.Trains.AlphaTrain" },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.type(c.getByLabelText("Input JSON"), "not json");
    await userEvent.click(c.getByRole("button", { name: "Run train" }));
    expect(await c.findByRole("alert")).toHaveTextContent("The input is not valid JSON.");
    expect(args.onClose).not.toHaveBeenCalled();
  },
};

// Cancel closes without running.
export const CancelCloses: Story = {
  args: { onClose: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByRole("button", { name: "Cancel" }));
    expect(args.onClose).toHaveBeenCalled();
  },
};

// Run bypasses a subject-keyed train's serialization; its dialog warns, in the Blazor words. A
// train without a subject key gets no warning.
export const SubjectKeyWarning: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = c.getByRole("combobox", { name: "Train" });
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBeGreaterThan(1));
    expect(c.queryByTestId("run-subject-bypass-warning")).not.toBeInTheDocument();
    await userEvent.selectOptions(select, "Trax.Trains.ZebraTrain");
    expect(c.getByTestId("run-subject-bypass-warning")).toHaveTextContent(
      "Run bypasses subject serialization. This train overrides QueueSubjectKey",
    );
    await userEvent.selectOptions(select, "Trax.Trains.AlphaTrain");
    expect(c.queryByTestId("run-subject-bypass-warning")).not.toBeInTheDocument();
  },
};
