import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { MachinePage } from "./MachinePage";
import { MACHINE_DEFINITIONS, MACHINE_IDS, machineQueryResolvers, machineScenario } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

const meta = {
  title: "Pages/Machine",
  component: MachinePage,
  parameters: { routePath: "/machines/:name", route: "/machines/topic-map", mock: machineScenario },
} satisfies Meta<typeof MachinePage>;

export default meta;
type Story = StoryObj<typeof meta>;

const instances = (c: ReturnType<typeof within>) => within(c.getByRole("region", { name: "Instances" }));
const rowCount = (c: ReturnType<typeof within>) => instances(c).getAllByRole("row").length - 1;
const node = (c: ReturnType<typeof within>, state: string) => c.getByRole("button", { name: new RegExp(`^${state}`) });

export const Mock: Story = {};

// The diagram: each state badged with its instances and stuck ones, the timer dashed with its
// delay, the state that runs a train linked to it; the instances under it with what each waits on.
export const Diagram: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "topic-map" })).toBeInTheDocument();
    await waitFor(() => expect(within(node(c, "ChoosingRange")).getByLabelText("Stuck: 1")).toBeInTheDocument());
    expect(within(node(c, "Building")).getByLabelText("Instances: 1")).toBeInTheDocument();
    expect(within(node(c, "Expired")).getByLabelText("Instances: 0")).toBeInTheDocument();
    expect(c.getByText("after 1 day")).toBeInTheDocument();
    expect(c.getByRole("link", { name: "IBuildTopicMapTrain" })).toHaveAttribute(
      "href",
      `/trains/${encodeURIComponent("Trax.Samples.Recovery.IBuildTopicMapTrain")}`,
    );
    await waitFor(() => expect(rowCount(c)).toBe(2));
    expect(instances(c).getByRole("link", { name: "Run 7110" })).toHaveAttribute("href", "/runs/7110");
    expect(instances(c).getByText("Overdue")).toBeInTheDocument();
    expect(instances(c).getByRole("link", { name: MACHINE_IDS.draftIdle })).toHaveAttribute(
      "href",
      `/machines/topic-map/user/${MACHINE_IDS.draftIdle}?row=22`,
    );
  },
};

// Clicking a state lists the instances in it.
export const StateFiltersTheList: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(rowCount(c)).toBe(2));
    await userEvent.click(node(c, "Building"));
    await waitFor(() => expect(rowCount(c)).toBe(1));
    expect(c.getByLabelText("State")).toHaveValue("Building");
  },
};

// Stuck only, from a link such as the machines list's stuck count.
export const StuckOnly: Story = {
  parameters: { route: "/machines/topic-map?stuck=true" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(rowCount(c)).toBe(1));
    expect(c.getByRole("checkbox", { name: "Stuck only" })).toBeChecked();
    expect(instances(c).getByText("Stuck")).toBeInTheDocument();
  },
};

// A machine with no instance has a diagram and says nothing fills its list yet.
export const NoInstances: Story = {
  parameters: { route: "/machines/retention-sweep" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("after 1 hour")).toBeInTheDocument();
    expect(await c.findByText(/retention-sweep has no instances/)).toBeInTheDocument();
  },
};

const noGraph: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      ...machineQueryResolvers,
      machines: () =>
        MACHINE_DEFINITIONS.map((m) =>
          m.name === "source-partition"
            ? { ...m, states: [], transitions: [], timers: [], initialState: null, schemaHash: null, graphUnavailableReason: "IR export refuses the machine." }
            : m,
        ),
    },
  }),
};

// A machine whose rules are code says why it has no diagram, and still lists its instances.
export const NoGraph: Story = {
  parameters: { route: "/machines/source-partition", mock: noGraph },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/source-partition has no diagram/)).toBeInTheDocument();
    expect(c.getByText("IR export refuses the machine.")).toBeInTheDocument();
    await waitFor(() => expect(rowCount(c)).toBe(3));
    // Its states are still listed with their counts, and filter the list.
    expect(c.getByText(/Instances by state/)).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Ingested" }));
    await waitFor(() => expect(rowCount(c)).toBe(1));
  },
};

// A name no machine has.
export const NotFound: Story = {
  parameters: { route: "/machines/no-such-machine" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("heading", { name: "Machine not found" })).toBeInTheDocument();
  },
};

// More instances than the API counts: the caption says the pager stops there.
export const CountCapped: Story = {
  parameters: {
    mock: {
      resolvers: () => ({
        OperationsQueries: {
          ...machineQueryResolvers,
          machineInstances: (_root: unknown, args: Record<string, unknown>) => ({
            ...(machineQueryResolvers.machineInstances(_root, args) as object),
            totalCount: 10_000,
            isCountCapped: true,
          }),
        },
      }),
    } satisfies MockSchemaOverrides,
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/More than 10,000 instances match/)).toBeInTheDocument();
  },
};

// A failed read is shown, not an empty list.
export const LoadError: Story = {
  parameters: {
    mock: {
      resolvers: () => ({
        OperationsQueries: {
          ...machineQueryResolvers,
          machineInstances: () => {
            throw new Error("Simulated backend error");
          },
        },
      }),
    } satisfies MockSchemaOverrides,
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};
