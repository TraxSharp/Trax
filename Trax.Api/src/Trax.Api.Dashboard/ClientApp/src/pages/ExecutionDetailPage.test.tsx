import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { executionDetailScenario } from "../mock/scenarios";

// The run page draws a run once: its chain on the shared graph, coloured by this run, with the
// selected step's panel, and the timeline below following the same selection.

afterEach(cleanup);

function renderRun(id: number) {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: executionDetailScenario });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[`/runs/${id}`]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
}

const panel = (graph: HTMLElement) => graph.querySelector("aside[data-node-id]") as HTMLElement | null;

test("a failed run opens on one graph, at the failed step, with its error in the panel", async () => {
  renderRun(902);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  await waitFor(() => expect(panel(graph)).toHaveAttribute("data-node-id", "Summarize#2"));

  // One graph, drawn by the shared component, and none of the other ways the page drew a run.
  expect(within(graph).getAllByRole("group", { name: "Steps of the train" })).toHaveLength(1);
  expect(graph.querySelectorAll(".react-flow__node")).toHaveLength(7);
  expect(screen.queryByText("Junction progress")).toBeNull();
  expect(screen.queryByRole("list", { name: "Declared steps of the train" })).toBeNull();

  const failed = graph.querySelector('.react-flow__node[data-id="Summarize#2"]') as HTMLElement;
  expect(within(failed).getByRole("button")).toHaveAttribute("aria-pressed", "true");
  expect(within(panel(graph)!).getByText("Transient · TimeoutException")).toBeInTheDocument();
  expect(within(panel(graph)!).getByText("Error")).toBeInTheDocument();
});

test("clicking a timeline bar selects the same node in the graph", async () => {
  renderRun(902);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  const timeline = await screen.findByRole("region", { name: "Junction timeline" });
  await waitFor(() => expect(timeline.querySelector('[data-position="2"]')).toHaveAttribute("data-selected", "true"));

  fireEvent.click(await within(timeline).findByRole("button", { name: "Step #0 ValidateOrder" }));

  await waitFor(() => expect(panel(graph)).toHaveAttribute("data-node-id", "Fetch#0"));
  const fetch = graph.querySelector('.react-flow__node[data-id="Fetch#0"]') as HTMLElement;
  expect(within(fetch).getByRole("button")).toHaveAttribute("aria-pressed", "true");
  expect(timeline.querySelector('[data-position="0"]')).toHaveAttribute("data-selected", "true");
  // A step no node recorded (the question at #1) selects nothing.
  expect(within(timeline.querySelector('[data-position="1"]') as HTMLElement).queryByRole("button")).toBeNull();
});

test("selecting a node in the graph highlights its bar in the timeline", async () => {
  renderRun(902);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  const timeline = await screen.findByRole("region", { name: "Junction timeline" });
  await waitFor(() => expect(graph.querySelector('.react-flow__node[data-id="Fetch#0"]')).not.toBeNull());

  fireEvent.click(within(graph.querySelector('.react-flow__node[data-id="Fetch#0"]') as HTMLElement).getByRole("button"));

  await waitFor(() => expect(timeline.querySelector('[data-position="0"]')).toHaveAttribute("data-selected", "true"));
  expect(timeline.querySelector('[data-position="2"]')).not.toHaveAttribute("data-selected");
});

test("a run under a chain that has changed since says so", async () => {
  renderRun(958);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  expect(await within(graph).findByRole("note")).toHaveTextContent("chain has changed since this run started");
});

test("a run under today's chain shows no notice", async () => {
  renderRun(952);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  await waitFor(() => expect(graph.querySelector(".react-flow__node")).not.toBeNull());
  expect(within(graph).queryByRole("note")).toBeNull();
});
