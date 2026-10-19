import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { StateMachinesPage } from "./StateMachinesPage";
import { errorOverride, machineScenario } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

const meta = {
  title: "Pages/Machines",
  component: StateMachinesPage,
  parameters: { route: "/machines", mock: machineScenario },
} satisfies Meta<typeof StateMachinesPage>;

export default meta;
type Story = StoryObj<typeof meta>;

const list = (c: ReturnType<typeof within>) => within(c.getByRole("region", { name: "Registered machines" }));
const row = (c: ReturnType<typeof within>, name: string) => list(c).getByRole("link", { name }).closest("tr")!;

export const Mock: Story = {};
export const Real: Story = { parameters: { real: true } };

// Every registered machine, the one with no instance included, with its instance and stuck counts.
export const Lists: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(list(c).getAllByRole("link", { name: /-/ })).toHaveLength(3));
    expect(list(c).getByRole("link", { name: "retention-sweep" })).toHaveAttribute("href", "/machines/retention-sweep");
    await waitFor(() => expect(within(row(c, "source-partition")).getByText("3")).toBeInTheDocument());
    expect(within(row(c, "retention-sweep")).getAllByText("0")).toHaveLength(2);
    expect(within(row(c, "topic-map")).getByRole("link", { name: "1 stuck" })).toHaveAttribute(
      "href",
      "/machines/topic-map?stuck=true",
    );
  },
};

const noMachines: MockSchemaOverrides = {
  resolvers: () => ({ OperationsQueries: { machines: () => [], machineInstanceCounts: () => [] } }),
};

// A host with no machines says what would fill the page.
export const Empty: Story = {
  parameters: { mock: noMachines },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/This host registers no state machines/)).toBeInTheDocument();
    expect(c.getByRole("link", { name: "State machines" })).toHaveAttribute("href", "https://traxsharp.net/docs/statemachine");
  },
};

// A failed read is shown, not an empty list.
export const LoadError: Story = {
  parameters: { mock: errorOverride("OperationsQueries", "machines") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};
