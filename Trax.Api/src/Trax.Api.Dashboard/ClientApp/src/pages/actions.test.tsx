import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { Provider } from "urql";
import type { Operation } from "@urql/core";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import {
  deadLetterScenario,
  executionDetailScenario,
  executionScenario,
  machineScenario,
  MACHINE_IDS,
} from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";
import { setAutoConfirm } from "../lib/confirm";

// What the action buttons send, and where they leave the operator: the variables of each mutation
// and the reads that follow it, counted at the client.

afterEach(cleanup);

// Most tests here are about what an action sends, so the confirmation answers itself; the ones
// about the dialog turn that off and press its buttons.
beforeEach(() => setAutoConfirm(true));
afterEach(() => setAutoConfirm(null));

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
  const { named } = renderAt(`/machines/source-partition/system/${MACHINE_IDS.dispatched}`, machineScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
  expect(await screen.findByText(/Cancellation requested for the run instance/)).toBeInTheDocument();
  expect(named("mutation", "CancelMachineInstance").map((op) => op.variables)).toEqual([
    { machine: "source-partition", ownerKind: "SYSTEM", id: MACHINE_IDS.dispatched },
  ]);
  const runs = within(screen.getByRole("region", { name: "Invoked runs" }));
  await waitFor(() => expect(runs.getByText("Cancellation requested")).toBeInTheDocument());
});

test("Cancel declined in the dialog sends nothing", async () => {
  setAutoConfirm(null);
  const { named } = renderAt(`/machines/source-partition/system/${MACHINE_IDS.dispatched}`, machineScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
  const dialog = await screen.findByRole("heading", { name: "Cancel the run it waits on" });
  expect(dialog).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Keep it" }));
  await waitFor(() => expect(screen.queryByRole("heading", { name: "Cancel the run it waits on" })).toBeNull());
  expect(named("mutation", "CancelMachineInstance")).toHaveLength(0);
});

test("Cancel confirmed in the dialog sends one cancel", async () => {
  setAutoConfirm(null);
  const { named } = renderAt(`/machines/source-partition/system/${MACHINE_IDS.dispatched}`, machineScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));
  fireEvent.click(await screen.findByRole("button", { name: "Cancel the run it waits on" }));
  await waitFor(() => expect(named("mutation", "CancelMachineInstance")).toHaveLength(1));
});

test("Resume sends no node, and opens the queued entry", async () => {
  const { named } = renderAt("/runs/902", executionDetailScenario);
  fireEvent.click(await screen.findByRole("button", { name: "Resume" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/runs\/queued\/\d+$/));
  expect(named("mutation", "ResumeExecution").map((op) => op.variables)).toEqual([{ id: 902, from: null }]);
});

test("Resume from here sends the node, at any depth", async () => {
  const leaf = "Plan#1/Fast/Mode#0/Lanes/Score#0/Lanes#0/Left/Check#0/Open/Pick#0/Deep/Leaf#0";
  const { named } = renderAt("/runs/956", executionDetailScenario);
  const graph = await screen.findByRole("region", { name: "Run graph" });
  // The page opens at the step that failed, six tracks deep; its panel offers the resume.
  await waitFor(() => expect(graph.querySelector(`aside[data-node-id="${leaf}"]`)).not.toBeNull());
  const panel = graph.querySelector(`aside[data-node-id="${leaf}"]`) as HTMLElement;
  fireEvent.click(within(panel).getByRole("button", { name: "Resume from here" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/runs\/queued\/\d+$/));
  expect(named("mutation", "ResumeExecution").map((op) => op.variables)).toEqual([{ id: 956, from: leaf }]);
});

// Bulk operations (api/0045): "all N matching" starts one over the list's filter, confirmed by
// typing the count, and opens its page.

test("Cancel all matching sends the list's filter and count, and the operation page shows its progress", async () => {
  const { named } = renderAt("/runs?state=IN_PROGRESS&train=Trax.Exec.GammaJob", executionScenario);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select #901" }));
  fireEvent.click(await screen.findByRole("button", { name: "Cancel all 5 matching" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/system\/operations\/[0-9a-f-]+$/));
  expect(named("mutation", "CancelExecutions")).toHaveLength(0);
  expect(named("mutation", "StartBulkOperation").map((op) => op.variables)).toEqual([
    {
      kind: "CANCEL",
      filter: {
        executions: {
          trainState: "IN_PROGRESS",
          trainName: "Trax.Exec.GammaJob",
          startedAfter: null,
          startedBefore: null,
          hideAdminTrains: false,
          failureClass: null,
          externalId: null,
          parentId: null,
          hostName: null,
        },
      },
      expectedCount: 5,
      askAfresh: false,
      note: null,
    },
  ]);
  const counts = within(await screen.findByRole("region", { name: "Counts" }));
  expect(counts.getByRole("progressbar", { name: "Progress" })).toHaveAttribute("aria-valuenow", "5");
  expect(within(counts.getByRole("group", { name: "Matched" })).getByText("5")).toBeInTheDocument();
  expect(within(counts.getByRole("group", { name: "Done" })).getByText("5")).toBeInTheDocument();
  expect(within(counts.getByRole("group", { name: "Failed" })).getByText("0")).toBeInTheDocument();
  const filter = within(screen.getByRole("region", { name: "Filter" }));
  expect(filter.getByText("Trax.Exec.GammaJob")).toBeInTheDocument();
  expect(filter.getByText("In progress")).toBeInTheDocument();
});

test("Cancel all matching is not offered over a filter that matches ended runs", async () => {
  renderAt("/runs", executionScenario);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select #901" }));
  expect(await screen.findByText(/Show only Pending or In progress runs to cancel all matching/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /Cancel all \d+ matching/ })).toBeNull();
});

test("All matching waits for the count to be typed, and sends nothing when declined", async () => {
  setAutoConfirm(null);
  const { named } = renderAt("/runs?state=IN_PROGRESS", executionScenario);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select #901" }));
  fireEvent.click(await screen.findByRole("button", { name: "Cancel all 5 matching" }));
  const confirm = await screen.findByRole("button", { name: "Cancel all matching runs" });
  expect(confirm).toBeDisabled();
  fireEvent.change(screen.getByLabelText("Type 5 to confirm"), { target: { value: "4" } });
  expect(confirm).toBeDisabled();
  fireEvent.change(screen.getByLabelText("Type 5 to confirm"), { target: { value: "5" } });
  expect(confirm).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "Keep it" }));
  await waitFor(() => expect(screen.queryByRole("button", { name: "Cancel all matching runs" })).toBeNull());
  expect(named("mutation", "StartBulkOperation")).toHaveLength(0);
});

test("Acknowledge all matching asks for the note and sends it with the dead-letter filter", async () => {
  const { named } = renderAt("/runs/dead-letters?status=AWAITING_INTERVENTION", deadLetterScenario);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select #702" }));
  fireEvent.click(await screen.findByRole("button", { name: "Acknowledge all 3 matching" }));
  const prompt = within(await screen.findByRole("dialog", { name: "Acknowledge all matching dead letters" }));
  fireEvent.change(prompt.getByRole("textbox"), { target: { value: "upstream outage" } });
  fireEvent.click(prompt.getByRole("button", { name: "Acknowledge" }));
  await waitFor(() => expect(screen.getByTestId("where").textContent).toMatch(/^\/system\/operations\//));
  expect(named("mutation", "StartBulkOperation").map((op) => op.variables)).toEqual([
    {
      kind: "ACKNOWLEDGE",
      filter: { deadLetters: { status: "AWAITING_INTERVENTION", manifestId: null } },
      expectedCount: 3,
      askAfresh: false,
      note: "upstream outage",
    },
  ]);
});
