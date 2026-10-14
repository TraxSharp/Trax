import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import type { ChangeDomain } from "../lib/useRefetchOnChange";

afterEach(cleanup);

// A host whose instance count drops from `before` to `after` once `shrink()` is called: the list
// pages by offset, so the page an operator is on can empty under them.
function shrinkingHost(before: number, after: number) {
  let total = before;
  let reads = 0;
  const row = (i: number) => ({
    machine: "source-partition",
    ownerKind: "SYSTEM",
    id: `3f2c1a00-0000-4000-8000-${String(i).padStart(12, "0")}`,
    rowId: i,
    state: "Ingesting",
    version: 1,
    createdAt: "2026-07-07T10:00:00.000Z",
    updatedAt: "2026-07-07T11:00:00.000Z",
    hasLiveInvokedRun: false,
  });
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        OperationsQueries: {
          machineInstanceCounts: () => [{ machine: "source-partition", state: "Ingesting", ownerKind: "SYSTEM", count: total }],
          machineInstances: (_root: unknown, args: Record<string, unknown>) => {
            reads += 1;
            const skip = Number(args.skip);
            const take = Number(args.take);
            const items = Array.from({ length: total }, (_, i) => row(i + 1)).slice(skip, skip + take);
            return { items, totalCount: total, isCountCapped: false };
          },
        },
      }),
    },
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/state-machines"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  return {
    shrink: () => {
      total = after;
    },
    reads: () => reads,
    signal: async (domain: ChangeDomain) => {
      await new Promise((r) => setTimeout(r, 0));
      store.publishEvent("OnDataChanged", { onDataChanged: { domain, timestamp: new Date(0).toISOString() } });
    },
  };
}

test("the list is read again when runs or the work queue change", async () => {
  const host = shrinkingHost(5, 5);
  expect(await screen.findByText("1–5 of 5")).toBeInTheDocument();
  const before = host.reads();
  await host.signal("EXECUTION");
  await waitFor(() => expect(host.reads()).toBeGreaterThan(before));
  const mid = host.reads();
  await host.signal("WORK_QUEUE");
  await waitFor(() => expect(host.reads()).toBeGreaterThan(mid));
});

test("a page that empties under the operator moves back to the last page that has rows", async () => {
  const host = shrinkingHost(25, 20);
  expect(await screen.findByText("1–20 of 25")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  expect(await screen.findByText("21–25 of 25")).toBeInTheDocument();
  host.shrink();
  await host.signal("EXECUTION");
  expect(await screen.findByText("1–20 of 20")).toBeInTheDocument();
  expect(screen.queryByText(/^21–/)).not.toBeInTheDocument();
  expect(screen.queryByText("No instances match.")).not.toBeInTheDocument();
});
