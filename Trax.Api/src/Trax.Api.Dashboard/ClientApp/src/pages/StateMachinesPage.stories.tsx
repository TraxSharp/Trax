import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { StateMachinesPage } from "./StateMachinesPage";
import { MACHINE_IDS, errorOverride, machineScenario, machineScenarioCapped } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

const meta = {
  title: "Pages/State machines",
  component: StateMachinesPage,
  parameters: { route: "/state-machines", mock: machineScenario },
} satisfies Meta<typeof StateMachinesPage>;

export default meta;
type Story = StoryObj<typeof meta>;

const counts = (c: ReturnType<typeof within>) => within(c.getByRole("region", { name: "Instances by state" }));
const instances = (c: ReturnType<typeof within>) => within(c.getAllByRole("table")[1]);
const rowCount = (c: ReturnType<typeof within>) => instances(c).getAllByRole("row").length - 1;

export const Mock: Story = {};
export const Real: Story = { parameters: { real: true } };

// The counts by machine, state and owner, and every instance newest first with its owner kind,
// state and whether it waits on a run. No column holds a context.
export const Lists: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(rowCount(c)).toBe(5));
    expect(counts(c).getAllByRole("row")).toHaveLength(5); // the header and four (machine, state, owner) groups
    const ingesting = counts(c).getByText("Ingesting").closest("tr")!;
    expect(within(ingesting).getByText("2")).toBeInTheDocument();
    expect(within(ingesting).getByText("System")).toBeInTheDocument();
    expect(instances(c).getAllByText("Yes")).toHaveLength(3);
    expect(instances(c).getByRole("link", { name: MACHINE_IDS.dispatched })).toHaveAttribute(
      "href",
      `/state-machines/source-partition/system/${MACHINE_IDS.dispatched}`,
    );
    // A user's draft is named by its row too.
    expect(instances(c).getByRole("link", { name: MACHINE_IDS.draftBuilding })).toHaveAttribute(
      "href",
      `/state-machines/topic-map/user/${MACHINE_IDS.draftBuilding}?row=21`,
    );
    expect(c.queryByText(/context/i, { selector: "th" })).not.toBeInTheDocument();
    expect(c.getByText("1–5 of 5")).toBeInTheDocument();
  },
};

// Clicking a count lists exactly those instances, and sets the filters to match.
export const CountFiltersTheList: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(rowCount(c)).toBe(5));
    await userEvent.click(counts(c).getByText("ChoosingRange").closest("tr")!.querySelector("button")!);
    await waitFor(() => expect(rowCount(c)).toBe(1));
    expect(c.getByLabelText("Machine")).toHaveValue("topic-map");
    expect(c.getByLabelText("State")).toHaveValue("ChoosingRange");
    expect(c.getByLabelText("Owner")).toHaveValue("USER");
  },
};

// The machine, state and owner filters narrow the list and compose; the states offered are the
// chosen machine's, and choosing another machine clears the state.
export const Filters: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(rowCount(c)).toBe(5));
    await userEvent.selectOptions(c.getByLabelText("Machine"), "source-partition");
    await waitFor(() => expect(rowCount(c)).toBe(3));
    const states = within(c.getByLabelText("State")).getAllByRole("option").map((o) => o.textContent);
    expect(states).toEqual(["Any state", "Ingested", "Ingesting"]);
    await userEvent.selectOptions(c.getByLabelText("State"), "Ingesting");
    await waitFor(() => expect(rowCount(c)).toBe(2));
    await userEvent.selectOptions(c.getByLabelText("Machine"), "topic-map");
    expect(c.getByLabelText("State")).toHaveValue("");
    await waitFor(() => expect(rowCount(c)).toBe(2));
    await userEvent.selectOptions(c.getByLabelText("Owner"), "SYSTEM");
    expect(await instances(c).findByText("No instances match.")).toBeInTheDocument();
  },
};

// More instances than the API counts: the caption says the pager stops there.
export const CountCapped: Story = {
  parameters: { mock: machineScenarioCapped },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/More than 10,000 instances match/)).toBeInTheDocument();
    expect(c.getByText(/of 10,000\+$/)).toBeInTheDocument();
  },
};

const noInstances: MockSchemaOverrides = {
  resolvers: () => ({
    OperationsQueries: {
      machineInstanceCounts: () => [],
      machineInstances: () => ({ items: [], totalCount: 0, isCountCapped: false, isEstimatedCount: false, skip: 0, take: 20, nextCursor: null }),
    },
  }),
};

// A host with no instances says so.
export const Empty: Story = {
  parameters: { mock: noInstances },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No state machine instances.")).toBeInTheDocument();
    expect(c.getByText("No instances match.")).toBeInTheDocument();
  },
};

// A failed read is shown, not an empty list.
export const LoadError: Story = {
  parameters: { mock: errorOverride("OperationsQueries", "machineInstances") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};
