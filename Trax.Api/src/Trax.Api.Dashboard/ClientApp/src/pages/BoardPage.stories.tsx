import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, within } from "storybook/test";
import { BoardPage } from "./BoardPage";
import { machineScenario, machineScenarioWithBoard } from "../mock/scenarios";

const meta = {
  title: "Pages/Board",
  component: BoardPage,
  parameters: { routePath: "/board/:machine", route: "/board/topic-map", mock: machineScenarioWithBoard },
} satisfies Meta<typeof BoardPage>;

export default meta;
type Story = StoryObj<typeof meta>;

// topic-map as paper notes, the other machines listed beside it. Clicking a note lists the scenarios behind it.
export const Board: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const board = await c.findByRole("region", { name: "Board of topic-map" });
    await userEvent.click(await within(board).findByRole("button", { name: "Build, command" }));
    expect(await within(board).findByText("Ada builds a map of a week")).toBeInTheDocument();
  },
};

// A machine its host does not serve from features: listed, and the page says it has no board.
export const NoBoard: Story = {
  parameters: { route: "/board/retention-sweep", mock: machineScenario },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/is not served from features/)).toBeInTheDocument();
  },
};
