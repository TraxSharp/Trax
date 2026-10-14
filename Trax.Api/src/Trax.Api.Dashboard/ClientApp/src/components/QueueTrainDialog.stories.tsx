import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, waitFor, within } from "storybook/test";
import { QueueTrainDialog } from "./QueueTrainDialog";
import { trainsScenario } from "../mock/scenarios";

const meta = {
  title: "Components/QueueTrainDialog",
  component: QueueTrainDialog,
  // The train dropdown reads the Trains fixture.
  parameters: { fixtures: true },
  args: { onClose: () => {}, onQueued: () => {} },
} satisfies Meta<typeof QueueTrainDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};

// Selecting a train with an input schema renders a labeled field per property, and queuing
// assembles them into the input JSON.
export const SchemaFields: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  args: { onClose: fn(), onQueued: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const select = c.getByRole("combobox");
    await waitFor(() =>
      expect(within(select).getAllByRole("option").length).toBeGreaterThan(1),
    );
    // ZebraTrain declares playerId + amount; Alpha/Mango take Unit (no fields).
    await userEvent.selectOptions(select, "Trax.Trains.ZebraTrain");
    await waitFor(() => expect(c.getByLabelText("Field playerId")).toBeInTheDocument());
    expect(c.getByLabelText("Field amount")).toBeInTheDocument();
    await userEvent.type(c.getByLabelText("Field playerId"), "player-42");
    await userEvent.type(c.getByLabelText("Field amount"), "5");
    await userEvent.click(c.getByRole("button", { name: "Queue train" }));
    await waitFor(() => expect(args.onQueued).toHaveBeenCalled());
  },
};

// A Unit-input train shows no schema fields.
export const NoFieldsForUnitInput: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = c.getByRole("combobox");
    await waitFor(() =>
      expect(within(select).getAllByRole("option").length).toBeGreaterThan(1),
    );
    await userEvent.selectOptions(select, "Trax.Trains.AlphaTrain");
    await waitFor(() => expect(c.queryByLabelText("Field playerId")).not.toBeInTheDocument());
  },
};

// Submit is disabled until a train is chosen, the priority field strips non-digits, and a
// successful queue calls onQueued then onClose.
export const SubmitGatedOnTrain: Story = {
  args: { onClose: fn(), onQueued: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const submit = c.getByRole("button", { name: "Queue train" });
    expect(submit).toBeDisabled();

    const select = c.getByRole("combobox");
    await waitFor(() =>
      expect(within(select).getAllByRole("option").length).toBeGreaterThan(1),
    );
    const firstTrain = within(select).getAllByRole("option")[1] as HTMLOptionElement;
    await userEvent.selectOptions(select, firstTrain.value);
    await waitFor(() => expect(submit).toBeEnabled());

    // Priority accepts digits only.
    const priority = c.getByDisplayValue("0");
    await userEvent.clear(priority);
    await userEvent.type(priority, "a1b2c");
    expect(priority).toHaveValue("12");

    await userEvent.click(submit);
    await waitFor(() => expect(args.onQueued).toHaveBeenCalled());
    expect(args.onClose).toHaveBeenCalled();
  },
};

// Re-selecting the placeholder option disables submit again.
export const DeselectingTrainDisablesSubmit: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const submit = c.getByRole("button", { name: "Queue train" });
    const select = c.getByRole("combobox");
    await waitFor(() =>
      expect(within(select).getAllByRole("option").length).toBeGreaterThan(1),
    );
    const firstTrain = within(select).getAllByRole("option")[1] as HTMLOptionElement;
    await userEvent.selectOptions(select, firstTrain.value);
    await waitFor(() => expect(submit).toBeEnabled());
    await userEvent.selectOptions(select, ""); // "Select a train…"
    await waitFor(() => expect(submit).toBeDisabled());
  },
};

// Clicking the backdrop closes without queuing; the card itself swallows the click.
export const BackdropCloses: Story = {
  args: { onClose: fn(), onQueued: fn() },
  play: async ({ canvasElement, args }) => {
    const backdrop = canvasElement.querySelector<HTMLElement>(".fixed.inset-0")!;
    await userEvent.click(backdrop);
    expect(args.onClose).toHaveBeenCalled();
    expect(args.onQueued).not.toHaveBeenCalled();
  },
};

// An enum property is a dropdown of its members and a boolean a checkbox; the queued JSON carries
// the member as given.
export const EnumAndBooleanFields: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  args: { onClose: fn(), onQueued: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const select = c.getByRole("combobox", { name: "Train" });
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBeGreaterThan(1));
    await userEvent.selectOptions(select, "Trax.Trains.ZebraTrain");
    const tier = await c.findByRole("combobox", { name: "Field tier" });
    expect(within(tier).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "(default)",
      "Bronze",
      "Silver",
      "Gold",
    ]);
    await userEvent.selectOptions(tier, "Gold");
    await userEvent.click(c.getByRole("checkbox", { name: "Field notify" }));
    expect(c.getByRole("checkbox", { name: "Field notify" })).toBeChecked();
    await userEvent.click(c.getByRole("button", { name: "Queue train" }));
    await waitFor(() => expect(args.onQueued).toHaveBeenCalled());
  },
};

// Opened for a train (from the Trains page), the train is preselected and submit is enabled.
export const Preselected: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  args: { initialTrain: "Trax.Trains.AlphaTrain" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("combobox", { name: "Train" })).toHaveValue("Trax.Trains.AlphaTrain"),
    );
    expect(c.getByRole("button", { name: "Queue train" })).toBeEnabled();
  },
};

// A refusal (here, input that is not JSON) keeps the dialog open and says why.
export const RefusalKeepsDialogOpen: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  args: { onClose: fn(), onQueued: fn(), initialTrain: "Trax.Trains.AlphaTrain" },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.type(c.getByLabelText("Input JSON"), "{{bad");
    await userEvent.click(c.getByRole("button", { name: "Queue train" }));
    expect(await c.findByRole("alert")).toHaveTextContent("The input is not valid JSON.");
    expect(args.onClose).not.toHaveBeenCalled();
    expect(args.onQueued).not.toHaveBeenCalled();
  },
};

// Queueing a subject-keyed train waits for its subject; the dialog says so, and only for that train.
export const SubjectKeyNote: Story = {
  parameters: { fixtures: false, mock: trainsScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = c.getByRole("combobox");
    await waitFor(() => expect(within(select).getAllByRole("option").length).toBeGreaterThan(1));
    await userEvent.selectOptions(select, "Trax.Trains.ZebraTrain");
    expect(c.getByTestId("queue-subject-note")).toHaveTextContent("its queued runs for one subject run one at a time");
    await userEvent.selectOptions(select, "Trax.Trains.MangoTrain");
    expect(c.queryByTestId("queue-subject-note")).not.toBeInTheDocument();
  },
};
