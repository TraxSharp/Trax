import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import type { Operation } from "@urql/core";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { executionScenario, workQueueScenario } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

// Runs is one area with four views (api/0043): All, Failed, Queued and Dead letters, one tab strip
// across them, and Failed is All runs filtered to state FAILED.

afterEach(cleanup);

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
      </MemoryRouter>
    </Provider>,
  );
  const named = (name: string) =>
    sent.filter((op) => op.query.definitions.some((d) => d.kind === "OperationDefinition" && d.name?.value === name));
  return { named };
}

const tabs = () => within(screen.getByRole("navigation", { name: "Run views" }));

test("every list of Runs shows the same four views, the current one marked", async () => {
  for (const [route, current] of [
    ["/runs", "All"],
    ["/runs?state=FAILED", "Failed"],
    ["/runs/queued", "Queued"],
    ["/runs/dead-letters", "Dead letters"],
  ]) {
    renderAt(route, executionScenario);
    await screen.findByRole("navigation", { name: "Run views" });
    expect(tabs().getAllByRole("link").map((l) => l.textContent)).toEqual(["All", "Failed", "Queued", "Dead letters"]);
    expect(tabs().getByRole("link", { name: current })).toHaveAttribute("aria-current", "page");
    expect(tabs().getAllByRole("link").filter((l) => l.getAttribute("aria-current"))).toHaveLength(1);
    cleanup();
  }
});

test("Failed is All runs filtered to state FAILED, with where and why each failed", async () => {
  const { named } = renderAt("/runs", executionScenario);
  expect(await screen.findByText("AlphaJob")).toBeInTheDocument();
  fireEvent.click(tabs().getByRole("link", { name: "Failed" }));
  expect(await screen.findByRole("heading", { name: "Failed runs" })).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByText("AlphaJob")).toBeNull());
  expect(named("Executions").at(-1)!.variables).toMatchObject({ trainState: "FAILED" });
  const beta = screen.getByText("BetaJob").closest("tr") as HTMLElement;
  expect(within(beta).getByText("ValidateStep")).toBeInTheDocument();
  expect(within(beta).getByText("Assertion failed")).toBeInTheDocument();
  expect(screen.getByLabelText("State")).toHaveValue("FAILED");
});

test("a run's row links the run, its manifest and its parent", async () => {
  renderAt("/runs", executionScenario);
  const beta = (await screen.findByText("BetaJob")).closest("tr") as HTMLElement;
  expect(within(beta).getByRole("link", { name: "#902" })).toHaveAttribute("href", "/runs/902");
  expect(within(beta).getByRole("link", { name: "#800" })).toHaveAttribute("href", "/runs/800");
});

test("a queued entry's row links the entry, its manifest and the run it started", async () => {
  renderAt("/runs/queued", workQueueScenario);
  const queued = (await screen.findByRole("checkbox", { name: "Select #601" })).closest("tr") as HTMLElement;
  expect(within(queued).getByRole("link", { name: "#601" })).toHaveAttribute("href", "/runs/queued/601");
  expect(within(queued).getByRole("link", { name: "#41" })).toHaveAttribute("href", "/schedules/41");
  const dispatched = screen.getByRole("link", { name: "#604" }).closest("tr") as HTMLElement;
  expect(within(dispatched).getByRole("link", { name: "#88001" })).toHaveAttribute("href", "/runs/88001");
});

test("an empty list says what fills it and links the docs", async () => {
  renderAt("/runs/queued", {
    resolvers: () => ({
      WorkQueueQueries: {
        workQueues: () => ({ items: [], totalCount: 0, isEstimatedCount: false, skip: 0, take: 25, nextCursor: null }),
      },
    }),
  });
  expect(await screen.findByText("No entries.")).toBeInTheDocument();
  expect(screen.getByText(/while a queued run waits for a worker/)).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Read more" })).toHaveAttribute(
    "href",
    "https://traxsharp.net/docs/scheduler/job-submission",
  );
});
