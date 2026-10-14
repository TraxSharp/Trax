import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";

// End-to-end proof that a list page actually refetches its grid on a change signal (not just that
// the hook fires): the mock's workQueues resolver returns a different train on the second call, so a
// refetch is observable as the row changing from FirstTrain to SecondTrain.
test("WorkQueuePage refetches its grid when a WORK_QUEUE signal fires", async () => {
  let call = 0;
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        WorkQueueQueries: {
          workQueues: () => {
            call += 1;
            const label = call === 1 ? "FirstTrain" : "SecondTrain";
            return {
              items: [
                {
                  id: 1,
                  externalId: "e1",
                  trainName: `Trax.Demo.Trains.${label}`,
                  status: "QUEUED",
                  createdAt: new Date(0).toISOString(),
                  priority: 0,
                  dispatchAttempts: 0,
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
      <MemoryRouter initialEntries={["/work-queue"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );

  // Initial load renders the first resolver result. Once it's on screen the page (and its
  // useRefetchOnChange subscription) is mounted.
  expect(await screen.findByText("FirstTrain")).toBeInTheDocument();

  // Let the subscription source finish subscribing, then publish one signal (a single event, so the
  // 300ms debounce isn't perpetually reset). It drives a network-only refetch that hits the resolver
  // again and swaps the row.
  await new Promise((r) => setTimeout(r, 0));
  store.publishEvent("OnDataChanged", {
    onDataChanged: { domain: "WORK_QUEUE", timestamp: new Date(0).toISOString() },
  });

  expect(await screen.findByText("SecondTrain")).toBeInTheDocument();
});
