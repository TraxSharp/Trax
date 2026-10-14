import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { TrainDetailPage } from "./TrainDetailPage";
import { trainDetailScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Train detail",
  component: TrainDetailPage,
  parameters: {
    routePath: "/trains/:name",
    route: `/trains/${encodeURIComponent("Trax.Demo.Trains.OrderTrain")}`,
    mock: trainDetailScenario,
  },
} satisfies Meta<typeof TrainDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    // Header shows the short name; stat cards and recent executions render from the scenario.
    await waitFor(() => expect(c.getAllByText("OrderTrain").length).toBeGreaterThan(0));
    expect(c.getByText("Success rate")).toBeInTheDocument();
    // The success-rate value fills in once the trainStats query resolves.
    await waitFor(() => expect(c.getByText("95%")).toBeInTheDocument()); // 400/420
  },
};

export const Real: Story = {
  parameters: { real: true, route: "/trains/Trax.Demo.Trains.OrderTrain" },
};
