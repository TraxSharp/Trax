import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, within } from "storybook/test";
import { Graph } from "./Graph";
import type { GraphEdgeSpec, GraphNodeSpec } from "../lib/graphLayout";

// A machine with a loop, the shape the hand-written layout could not draw: Draft → Review →
// Approved, with Review able to send the document back.
const states: GraphNodeSpec[] = [
  { id: "Draft", label: "Draft", initial: true, badges: [{ label: "4", tone: "info", title: "Instances" }] },
  {
    id: "Review",
    label: "Review",
    detail: "Invokes ReviewDocument",
    tone: "info",
    badges: [
      { label: "12", tone: "info", title: "Instances" },
      { label: "2", tone: "warn", title: "Stuck" },
    ],
  },
  { id: "Approved", label: "Approved", final: true, tone: "ok" },
];
const transitions: GraphEdgeSpec[] = [
  { id: "submit", source: "Draft", target: "Review", label: "Submit" },
  { id: "approve", source: "Review", target: "Approved", label: "approved" },
  { id: "reject", source: "Review", target: "Draft", label: "rejected", tone: "danger" },
  { id: "remind", source: "Review", target: "Review", label: "after 2d", dashed: true },
];

const meta = {
  title: "Components/Graph",
  component: Graph,
  args: { nodes: states, edges: transitions, ariaLabel: "States of Approval" },
} satisfies Meta<typeof Graph>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Machine: Story = {
  args: {
    legend: [
      { label: "Has instances", tone: "info" },
      { label: "Final", tone: "ok" },
    ],
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const graph = c.getByRole("group", { name: "States of Approval" });
    for (const name of ["Draft", "Review", "Approved"]) await expect(within(graph).getByText(name)).toBeInTheDocument();
    await expect(within(graph).getByLabelText("Stuck: 2")).toBeInTheDocument();
    // Every transition is drawn, the loop back to Draft and the self-transition included.
    await expect(graph.querySelectorAll(".react-flow__edge")).toHaveLength(transitions.length);
    await expect(c.getByRole("list", { name: "Legend" })).toBeInTheDocument();
  },
};

export const Selectable: Story = {
  args: { onSelect: fn(), selectedId: "Review" },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await expect(c.getByRole("button", { name: /Review/ })).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(c.getByRole("button", { name: /Approved/ }));
    await expect(args.onSelect).toHaveBeenCalledWith("Approved");
  },
};

export const Empty: Story = {
  args: { nodes: [], edges: [], emptyText: "No dependency graph to display." },
  play: async ({ canvasElement }) => {
    await expect(within(canvasElement).getByText("No dependency graph to display.")).toBeInTheDocument();
  },
};
