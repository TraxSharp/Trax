import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { Provider } from "urql";
import type { Operation } from "@urql/core";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { executionDetailScenario, machineScenario, MACHINE_IDS } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

// What the action buttons send, and where they leave the operator: the variables of each mutation
// and the reads that follow it, counted at the client.

afterEach(cleanup);

let confirmSpy: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
});
afterEach(() => confirmSpy.mockRestore());

function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>;
}

function renderAt(route: string, overrides: MockSchemaOverrides) {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides });
  const sent: Operation[] = [];
  const execute = client.executeRequestOperation.bind(client);
  vi.spyOn(client, "executeRequestOperation").mockImplementation((op) => {
    sent.push(op);
    return execute(op);
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
        <Where />
      </MemoryRouter>
    </Provider>,
  );
  const named = (kind: Operation["kind"], name: string) =>
    sent.filter(
      (op) =>
        op.kind === kind &&
        op.query.definitions.some((d) => d.kind === "OperationDefinition" && d.name?.value === name),
    );
  return { named };
}

test("Cancel on a system instance sends that instance, and reads it again to show the cancel", async () => {
  const { named } = renderAt(`/state-machines/source-partition/system/${MACHINE_IDS.dispatched}`, machineScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
  expect(await screen.findByText(/Cancellation requested for the run instance/)).toBeInTheDocument();
  expect(named("mutation", "CancelMachineInstance").map((op) => op.variables)).toEqual([
    { machine: "source-partition", ownerKind: "SYSTEM", id: MACHINE_IDS.dispatched },
  ]);
  const runs = within(screen.getByRole("region", { name: "Invoked runs" }));
  await waitFor(() => expect(runs.getByText("Cancellation requested")).toBeInTheDocument());
});

test("Cancel declined at the prompt sends nothing", async () => {
  confirmSpy.mockReturnValue(false);
  const { named } = renderAt(`/state-machines/source-partition/system/${MACHINE_IDS.dispatched}`, machineScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
  await new Promise((r) => setTimeout(r, 50));
  expect(named("mutation", "CancelMachineInstance")).toHaveLength(0);
});

test("Resume sends no node, and opens the queued entry", async () => {
  const { named } = renderAt("/executions/902", executionDetailScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Resume" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/work-queue\/\d+$/));
  expect(named("mutation", "ResumeExecution").map((op) => op.variables)).toEqual([{ id: 902, from: null }]);
});

test("Resume from here sends the node, at any depth", async () => {
  const leaf = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0/Deep/Leaf#0";
  const { named } = renderAt("/executions/956", executionDetailScenario);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  await waitFor(() => expect(graph.querySelector(`[data-node-id="${leaf}"]`)).not.toBeNull());
  const node = graph.querySelector(`[data-node-id="${leaf}"]`) as HTMLElement;
  // The node's own button, not one of a node nested under it.
  fireEvent.click(within(node.firstElementChild as HTMLElement).getByRole("button", { name: "Resume from here" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/work-queue\/\d+$/));
  expect(named("mutation", "ResumeExecution").map((op) => op.variables)).toEqual([{ id: 956, from: leaf }]);
});
