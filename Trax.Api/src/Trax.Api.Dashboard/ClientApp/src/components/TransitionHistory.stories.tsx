import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, within } from "storybook/test";
import { TransitionHistory } from "./TransitionHistory";
import { MACHINE_IDS, machineScenario } from "../mock/scenarios";
import type { MachineInstanceTransition } from "../types";

const meta = {
  title: "Components/Transition history",
  component: TransitionHistory,
  parameters: { mock: machineScenario },
} satisfies Meta<typeof TransitionHistory>;

export default meta;
type Story = StoryObj<typeof meta>;

// A system instance that retried its ingest many times: the first page, then the older ones.
export const LongHistory: Story = {
  args: { instance: { machine: "source-partition", ownerKind: "SYSTEM", id: MACHINE_IDS.dispatched } },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const history = within(await c.findByRole("region", { name: "History" }));
    expect(await history.findByText("started run 7101 (in progress)")).toBeInTheDocument();
    expect(history.getAllByRole("listitem")).toHaveLength(25);
    await userEvent.click(history.getByRole("button", { name: "Load older" }));
    expect(await history.findByText("entered Pending")).toBeInTheDocument();
    expect(history.getAllByRole("listitem")).toHaveLength(38);
    expect(history.queryByRole("button", { name: "Load older" })).toBeNull();
  },
};

// A user's draft: the owner's start, save and builds, the failed build's outcome.
export const UserDraft: Story = {
  args: { instance: { machine: "topic-map", ownerKind: "USER", id: MACHINE_IDS.draftBuilding, rowId: 21 } },
  play: async ({ canvasElement }) => {
    const history = within(await within(canvasElement).findByRole("region", { name: "History" }));
    expect(await history.findByText("Building.failed")).toBeInTheDocument();
    expect(history.getByText("stayed in ChoosingRange")).toBeInTheDocument();
    expect(history.getAllByText("by Owner").length).toBeGreaterThan(0);
  },
};

const at = (minute: number) => new Date(Date.UTC(2026, 6, 7, 10, minute)).toISOString();

// Rows already read: a timer's fire and a spawned child, each kind once.
const ROWS: MachineInstanceTransition[] = [
  { revision: 4, kind: "COMMAND", trigger: "Split", actor: "HOST", fromState: "Waiting", toState: "Splitting", at: at(30), previousAt: at(20), startedRun: null, startedChild: { machine: "source-partition", ownerKind: "SYSTEM", id: MACHINE_IDS.idle, rowId: 13 } },
  { revision: 3, kind: "TIMER", trigger: "Idle.after", actor: "MACHINE", fromState: "Idle", toState: "Waiting", at: at(20), previousAt: at(10), startedRun: { executionId: null, trainState: "PENDING", workQueueEntryId: 7201 }, startedChild: null },
  { revision: 2, kind: "OUTCOME", trigger: null, actor: "MACHINE", fromState: "Idle", toState: "Idle", at: at(10), previousAt: at(0), startedRun: null, startedChild: null },
  { revision: 1, kind: "START", trigger: null, actor: "HOST", fromState: null, toState: "Idle", at: at(0), previousAt: null, startedRun: null, startedChild: null },
];

export const TimerAndChild: Story = {
  args: { transitions: ROWS },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByRole("link", { name: "started source-partition" })).toHaveAttribute(
      "href",
      `/machines/source-partition/system/${MACHINE_IDS.idle}`,
    );
    expect(c.getByRole("link", { name: "queued entry 7201 (pending)" })).toHaveAttribute("href", "/runs/queued/7201");
    expect(c.getAllByText("10m 00s in Idle")).toHaveLength(2);
    expect(c.getByText("10m 00s in Waiting")).toBeInTheDocument();
    expect(c.getByText("Run outcome")).toBeInTheDocument();
    expect(c.getByText("entered Idle")).toBeInTheDocument();
  },
};

export const Empty: Story = {
  args: { transitions: [] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText(/Every start, command, save, timer and run outcome is recorded/)).toBeInTheDocument();
    expect(c.getByRole("link", { name: "How state machines move" })).toHaveAttribute(
      "href",
      "https://traxsharp.net/docs/statemachine",
    );
  },
};
