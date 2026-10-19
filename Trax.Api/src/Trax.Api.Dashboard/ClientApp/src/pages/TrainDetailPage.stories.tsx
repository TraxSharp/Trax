import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { TrainDetailPage } from "./TrainDetailPage";
import { TRAIN_DETAIL_TRAIN, TRAIN_NEVER_RAN, trainDetailScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Train detail",
  component: TrainDetailPage,
  parameters: {
    routePath: "/trains/:name",
    route: `/trains/${encodeURIComponent(TRAIN_DETAIL_TRAIN)}`,
    mock: trainDetailScenario,
  },
} satisfies Meta<typeof TrainDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getAllByText("ZebraTrain").length).toBeGreaterThan(0));
    await waitFor(() => expect(c.getByText("95%")).toBeInTheDocument()); // 400/420
    // The chain, with the range's failures on the step they stopped at.
    const chain = await c.findByRole("group", { name: "Chain of ZebraTrain" });
    await waitFor(() =>
      expect(within(chain).getByLabelText("Failed runs in the last 24 hours: 3")).toBeInTheDocument(),
    );
    // The grid: a failed run's Summarize cell opens that run.
    await waitFor(() =>
      expect(c.getByRole("link", { name: "Run #959, Summarize: Failed" })).toHaveAttribute("href", "/runs/959"),
    );
    await waitFor(() => expect(c.getByText("Every 5 minutes")).toBeInTheDocument());
    expect(c.getByRole("link", { name: "Instances of zebra-feeder in Feeding" })).toBeInTheDocument();
  },
};

export const NeverRan: Story = {
  parameters: { route: `/trains/${encodeURIComponent(TRAIN_NEVER_RAN)}` },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByText("This train has not run yet.");
    expect(c.getByRole("button", { name: "Run now" })).toBeInTheDocument();
    expect(c.getByRole("button", { name: "Queue a run" })).toBeInTheDocument();
    const chain = await c.findByRole("group", { name: "Chain of AlphaTrain" });
    await waitFor(() => expect(within(chain).getByText("Summarize")).toBeInTheDocument());
  },
};

export const Real: Story = {
  parameters: { real: true, route: "/trains/Trax.Demo.Trains.OrderTrain" },
};
