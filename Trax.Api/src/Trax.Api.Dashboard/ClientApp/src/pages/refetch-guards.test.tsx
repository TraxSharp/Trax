import { render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { cleanup } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { executionDetailScenario, machineScenario, MACHINE_IDS } from "../mock/scenarios";
import { limitAnswers } from "../lib/answerable";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";
import type { ChangeDomain } from "../lib/useRefetchOnChange";

// A change signal refetches what a page shows, but never a read the page would not send: a route
// the API cannot look up, or a part the client cannot answer. urql's reexecute ignores `pause`, so
// each page guards its own refetch; these count what reaches the resolvers.

afterEach(cleanup);

type Resolvers = Record<string, Record<string, (...args: unknown[]) => unknown>>;

// A scenario's resolvers, which it may give as an object or as a function of the mock store.
function resolversOf(scenario: MockSchemaOverrides, store: unknown): Resolvers {
  const given = scenario.resolvers as unknown;
  return ((typeof given === "function" ? given(store) : given) ?? {}) as Resolvers;
}

// Wraps a scenario so every call of `field` on OperationsQueries is counted.
function counting(scenario: MockSchemaOverrides, field: string, calls: { n: number }): MockSchemaOverrides {
  return {
    ...scenario,
    resolvers: (store) => {
      const base = resolversOf(scenario, store);
      const ops = base.OperationsQueries ?? {};
      const inner = ops[field];
      return {
        ...base,
        OperationsQueries: {
          ...ops,
          [field]: (...args: unknown[]) => {
            calls.n += 1;
            return inner ? inner(...args) : null;
          },
        },
      };
    },
  };
}

function renderAt(route: string, overrides: MockSchemaOverrides, limit?: (client: ReturnType<typeof createMockClient>) => void) {
  const store = createMockStore();
  const client = createMockClient({ store, fixtures: false, overrides });
  limit?.(client);
  // Every query the page sends, by operation name, whether or not a resolver runs for it (a request
  // the API refuses for its variables never reaches one).
  const sent: string[] = [];
  const execute = client.executeRequestOperation.bind(client);
  vi.spyOn(client, "executeRequestOperation").mockImplementation((op) => {
    if (op.kind === "query") {
      const def = op.query.definitions.find((d) => d.kind === "OperationDefinition");
      if (def?.kind === "OperationDefinition" && def.name) sent.push(def.name.value);
    }
    return execute(op);
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  return { store, sent: (name: string) => sent.filter((n) => n === name).length };
}

// One signal, after the page's subscription is up, then past the 300ms debounce.
async function signal(store: ReturnType<typeof createMockStore>, domain: ChangeDomain) {
  await new Promise((r) => setTimeout(r, 0));
  store.publishEvent("OnDataChanged", { onDataChanged: { domain, timestamp: new Date(0).toISOString() } });
  await new Promise((r) => setTimeout(r, 450));
}

test("a state machine route the API cannot look up sends no machineInstance read on a change", async () => {
  const calls = { n: 0 };
  const { store, sent } = renderAt(
    `/state-machines/topic-map/user/${MACHINE_IDS.draftBuilding}`,
    counting(machineScenario, "machineInstance", calls),
  );
  expect(await screen.findByText(/named by its row as well as its id/)).toBeInTheDocument();
  await signal(store, "EXECUTION");
  await signal(store, "WORK_QUEUE");
  expect(sent("MachineInstance")).toBe(0);
  expect(calls.n).toBe(0);
});

test("an unknown owner segment sends no machineInstance read on a change", async () => {
  const calls = { n: 0 };
  const { store, sent } = renderAt(
    `/state-machines/topic-map/admin/${MACHINE_IDS.draftBuilding}`,
    counting(machineScenario, "machineInstance", calls),
  );
  expect(await screen.findByText(/is not an owner/)).toBeInTheDocument();
  await signal(store, "EXECUTION");
  expect(sent("MachineInstance")).toBe(0);
  expect(calls.n).toBe(0);
});

test("a state machine instance the API can look up is refetched on a change", async () => {
  const calls = { n: 0 };
  const { store } = renderAt(
    `/state-machines/source-partition/system/${MACHINE_IDS.dispatched}`,
    counting(machineScenario, "machineInstance", calls),
  );
  expect(await screen.findByRole("heading", { name: "source-partition" })).toBeInTheDocument();
  const before = calls.n;
  await signal(store, "EXECUTION");
  expect(calls.n).toBe(before + 1);
});

test("a client that cannot answer the run graph is never sent one on a change", async () => {
  const calls = { n: 0 };
  const { store, sent } = renderAt("/executions/903", counting(executionDetailScenario, "runGraph", calls), (client) =>
    limitAnswers(client, ["ExecutionDetail", "JunctionRuns", "Decisions", "Logs", "ExecutionChildren"]),
  );
  expect(await screen.findByRole("heading", { name: "AlphaJob" })).toBeInTheDocument();
  await signal(store, "EXECUTION");
  expect(sent("RunGraph")).toBe(0);
  expect(calls.n).toBe(0);
});

test("a finished run's graph is not read again on a change", async () => {
  const calls = { n: 0 };
  const { store } = renderAt("/executions/952", counting(executionDetailScenario, "runGraph", calls));
  expect(await screen.findByRole("region", { name: "Run graph" })).toBeInTheDocument();
  const before = calls.n;
  expect(before).toBeGreaterThan(0);
  await signal(store, "EXECUTION");
  expect(calls.n).toBe(before);
});

test("an active run's graph is read again on a change", async () => {
  const calls = { n: 0 };
  const { store } = renderAt("/executions/903", counting(executionDetailScenario, "runGraph", calls));
  expect(await screen.findByRole("heading", { name: "AlphaJob" })).toBeInTheDocument();
  await new Promise((r) => setTimeout(r, 50));
  const before = calls.n;
  await signal(store, "EXECUTION");
  expect(calls.n).toBe(before + 1);
});

test("a run's graph is read once more when the run ends, and not after", async () => {
  const graphCalls = { n: 0 };
  let detailCalls = 0;
  const scenario = counting(executionDetailScenario, "runGraph", graphCalls);
  const ends: MockSchemaOverrides = {
    resolvers: (store) => {
      const base = resolversOf(scenario, store);
      const ops = base.OperationsQueries;
      return {
        ...base,
        OperationsQueries: {
          ...ops,
          // In progress on the first read, finished from the second on.
          executionDetail: (...args: unknown[]) => {
            detailCalls += 1;
            const run = ops.executionDetail(...args) as Record<string, unknown>;
            return detailCalls === 1 ? run : { ...run, trainState: "COMPLETED", endTime: "2026-07-07T11:55:12.000Z" };
          },
        },
      };
    },
  };
  const { store } = renderAt("/executions/903", ends);
  expect(await screen.findByRole("heading", { name: "AlphaJob" })).toBeInTheDocument();
  await new Promise((r) => setTimeout(r, 50));
  const before = graphCalls.n;
  // The change that ends the run: the run and its graph are read, then the graph once more for the
  // state that moved.
  await signal(store, "EXECUTION");
  expect(graphCalls.n).toBeGreaterThan(before);
  const ended = graphCalls.n;
  await signal(store, "EXECUTION");
  expect(graphCalls.n).toBe(ended);
});
