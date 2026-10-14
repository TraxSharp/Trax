import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";

// A cancellation request only sets cancellationRequested on the run; no lifecycle event fires until
// the runner observes it. The EXECUTION change signal is what tells the runs grid to refetch.
test("ExecutionsPage refetches its grid when an EXECUTION signal fires", async () => {
  let call = 0;
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        OperationsQueries: {
          executions: () => {
            call += 1;
            const label = call === 1 ? "FirstTrain" : "SecondTrain";
            return {
              items: [
                {
                  id: 1,
                  externalId: "e1",
                  name: `Trax.Demo.Trains.${label}`,
                  trainState: "IN_PROGRESS",
                  startTime: new Date(0).toISOString(),
                  endTime: null,
                  failureJunction: null,
                  failureReason: null,
                  manifestId: null,
                  cancellationRequested: call > 1,
                  hostName: null,
                  hostEnvironment: null,
                  hostInstanceId: null,
                },
              ],
              totalCount: 1,
              skip: 0,
              take: 25,
              isEstimatedCount: false,
              nextCursor: null,
            };
          },
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

  expect(await screen.findByText("FirstTrain")).toBeInTheDocument();

  await new Promise((r) => setTimeout(r, 0));
  store.publishEvent("OnDataChanged", {
    onDataChanged: { domain: "EXECUTION", timestamp: new Date(0).toISOString() },
  });

  expect(await screen.findByText("SecondTrain")).toBeInTheDocument();
});
