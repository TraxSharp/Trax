import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, within } from "storybook/test";
import { MachineBoard } from "./MachineBoard";
import type { Board } from "../lib/machineBoard";

const scenario = (line: number, name: string) => ({ file: "ordering.feature", line, scenario: name });

const board: Board = {
  machine: "order",
  lanes: ["order", "shop"],
  stickies: [
    { id: "actor:customer", kind: "actor", machine: "order", label: "customer", scenarios: [] },
    { id: "command:order:Place", kind: "command", machine: "order", label: "Place", scenarios: [scenario(6, "Ada places her order")] },
    { id: "event:order:Draft:Place:Placed", kind: "event", machine: "order", label: "Draft → Placed", scenarios: [scenario(6, "Ada places her order")] },
    { id: "aggregate:order", kind: "aggregate", machine: "order", label: "order", scenarios: [scenario(2, "Ada has an order")] },
    { id: "policy:order:Placed:shop:Ring", kind: "policy", machine: "shop", label: "when order is Placed, Ring shop", scenarios: [scenario(12, "The shop hears of it")] },
    { id: "command:shop:Ring", kind: "command", machine: "shop", label: "Ring", scenarios: [scenario(12, "The shop hears of it")] },
    { id: "view:orders", kind: "view", machine: "order", label: "orders", scenarios: [scenario(9, "Ada reads her orders")] },
  ],
  links: [
    { from: "actor:customer", to: "command:order:Place" },
    { from: "command:order:Place", to: "event:order:Draft:Place:Placed" },
    { from: "event:order:Draft:Place:Placed", to: "aggregate:order" },
    { from: "event:order:Draft:Place:Placed", to: "policy:order:Placed:shop:Ring" },
    { from: "policy:order:Placed:shop:Ring", to: "command:shop:Ring" },
    { from: "aggregate:order", to: "view:orders" },
  ],
};

const meta = {
  title: "Components/Machine board",
  component: MachineBoard,
} satisfies Meta<typeof MachineBoard>;

export default meta;
type Story = StoryObj<typeof meta>;

// Clicking a command lists the scenarios that put it there, by file, line and name.
export const WithProvenance: Story = {
  args: { board },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await userEvent.click(await c.findByRole("button", { name: /Place/ }));
    await expect(await c.findByText("Ada places her order")).toBeInTheDocument();
    await expect(c.getByText("ordering.feature:6")).toBeInTheDocument();
  },
};

// A host file with no provenance: the board, and a line saying it records no scenarios.
export const WithoutProvenance: Story = {
  args: { board: { ...board, stickies: board.stickies.map((s) => ({ ...s, scenarios: [] })) } },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await expect(await c.findByText(/records no scenarios/)).toBeInTheDocument();
  },
};
