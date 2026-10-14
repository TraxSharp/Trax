import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { setHideAdminTrains } from "../lib/adminTrains";

const ADMIN = "Trax.Scheduler.Trains.JobDispatcher.IJobDispatcherTrain";

function execRow(id: number, name: string) {
  return {
    id,
    externalId: `e${id}`,
    name,
    trainState: "COMPLETED",
    startTime: new Date(0).toISOString(),
    endTime: new Date(1000).toISOString(),
    failureJunction: null,
    failureReason: null,
    manifestId: null,
    hostName: null,
  };
}

function page(items: unknown[]) {
  return {
    items,
    totalCount: items.length,
    isEstimatedCount: false,
    skip: 0,
    take: 25,
    nextCursor: null,
  };
}

function renderExecutions(baseRows: unknown[]) {
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        OperationsQueries: {
          executions: () => page(baseRows),
          adminTrainNames: () => [ADMIN],
        },
      }),
    },
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/executions"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

function stateEvent(metadataId: number, trainName: string) {
  return {
    onTrainStateChanged: {
      metadataId,
      externalId: `ext-${metadataId}`,
      trainName,
      trainState: "IN_PROGRESS",
      timestamp: new Date(0).toISOString(),
      failureJunction: null,
      failureReason: null,
      hostName: null,
      hostEnvironment: null,
      output: null,
    },
  };
}

beforeEach(() => setHideAdminTrains(true));
afterEach(() => setHideAdminTrains(true));

test("hides admin-train events from the feed and table, and reveals them when toggled off", async () => {
  const store = renderExecutions([execRow(1, "Trax.Demo.Trains.SeedTrain")]);
  await screen.findByText("SeedTrain");

  await new Promise((r) => setTimeout(r, 0));
  store.publishEvent("OnTrainStateChanged", stateEvent(9001, ADMIN));
  store.publishEvent("OnTrainStateChanged", stateEvent(9002, "Trax.Demo.Trains.VisibleTrain"));

  // The non-admin event streams in (feed + prepended table row); the admin one is filtered out of
  // both.
  expect(await screen.findAllByText("VisibleTrain")).not.toHaveLength(0);
  expect(screen.queryAllByText("IJobDispatcherTrain")).toHaveLength(0);

  // Turning the toggle off reveals the admin train.
  fireEvent.click(screen.getByRole("checkbox", { name: /hide admin trains/i }));
  expect(await screen.findAllByText("IJobDispatcherTrain")).not.toHaveLength(0);
});

test("caps the live-merged first page to the page size", async () => {
  const base = Array.from({ length: 25 }, (_, i) =>
    execRow(i + 1, `Trax.Demo.Trains.Base${i}`),
  );
  const store = renderExecutions(base);
  await screen.findByText("Base0");

  await new Promise((r) => setTimeout(r, 0));
  // Three brand-new (non-admin) executions arrive over the wire.
  for (let i = 0; i < 3; i++)
    store.publishEvent("OnTrainStateChanged", stateEvent(1000 + i, `Trax.Demo.Trains.Live${i}`));

  // The newest prepend is visible; the oldest base rows fall off so the grid never exceeds a page.
  await screen.findAllByText("Live2");
  const table = screen.getByRole("table");
  // 1 header row + at most PAGE_SIZE (25) body rows.
  expect(within(table).getAllByRole("row").length).toBe(26);
  expect(screen.queryByText("Base24")).not.toBeInTheDocument();
});
