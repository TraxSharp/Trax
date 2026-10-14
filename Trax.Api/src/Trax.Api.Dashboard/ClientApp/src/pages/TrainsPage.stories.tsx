import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { TrainsPage } from "./TrainsPage";
import { trainsScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Trains",
  component: TrainsPage,
  parameters: { route: "/trains", mock: trainsScenario },
} satisfies Meta<typeof TrainsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

const firstRow = (c: ReturnType<typeof within>) => c.getAllByRole("row")[1];

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// The name filter narrows the (in-memory) registry.
export const FilterByName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaTrain")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Filter by name…"), "Mango");
    await waitFor(() => expect(c.queryByText("AlphaTrain")).not.toBeInTheDocument());
    expect(c.getByText("MangoTrain")).toBeInTheDocument();
  },
};

// Clicking the Train column header toggles the sort direction.
export const SortByName: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("AlphaTrain")).toBeInTheDocument());
    expect(within(firstRow(c)).getByText("AlphaTrain")).toBeInTheDocument(); // asc by default
    await userEvent.click(c.getByText("Train"));
    await waitFor(() => expect(within(firstRow(c)).getByText("ZebraTrain")).toBeInTheDocument());
  },
};

// Queue from the Trains page opens the queue dialog for that train; queuing closes it.
export const QueueFromTrains: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("ZebraTrain")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Queue ZebraTrain" }));
    await waitFor(() => expect(c.getByText("Queue a train")).toBeInTheDocument());
    expect(c.getByRole("combobox", { name: "Train" })).toHaveValue("Trax.Trains.ZebraTrain");
    await userEvent.type(c.getByLabelText("Field playerId"), "player-1");
    await userEvent.click(c.getByRole("button", { name: "Queue train" }));
    await waitFor(() => expect(c.queryByText("Queue a train")).not.toBeInTheDocument());
  },
};

// Run from the Trains page opens the run dialog for that train; running closes it.
export const RunFromTrains: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("MangoTrain")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Run MangoTrain" }));
    await waitFor(() => expect(c.getByText("Run a train")).toBeInTheDocument());
    expect(c.getByRole("combobox", { name: "Train" })).toHaveValue("Trax.Trains.MangoTrain");
    await userEvent.click(c.getByRole("button", { name: "Run train" }));
    await waitFor(() => expect(c.queryByText("Run a train")).not.toBeInTheDocument());
  },
};
