import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { attentionNeedsWork, overviewScenario } from "../mock/scenarios";

afterEach(cleanup);

function renderOverview(overrides = overviewScenario(attentionNeedsWork, { runs: true })) {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
}

test("a paused dispatcher and a dead-letter group lead the Overview, linking to System and to the filtered dead letters", async () => {
  renderOverview();

  const attention = within(await screen.findByRole("region", { name: "Needs attention" }));
  const paused = await attention.findByRole("link", { name: /Job dispatcher paused/ });
  expect(paused).toHaveAttribute("href", "/system/scheduler");

  const group = attention.getByRole("link", { name: /2 dead letters of IChargeCardTrain/ });
  expect(group).toHaveTextContent("Max retries exceeded: (4) failures > (3) max retries");
  const href = new URL(group.getAttribute("href")!, "http://console");
  expect(href.pathname).toBe("/runs/dead-letters");
  expect(Object.fromEntries(href.searchParams)).toEqual({
    status: "AWAITING_INTERVENTION",
    train: "Trax.Samples.Billing.IChargeCardTrain",
    reason: "Max retries exceeded: (4) failures > (3) max retries",
  });

  // The attention list comes before the charts, and the host's process stats are gone.
  const heading = screen.getByRole("heading", { name: "Needs attention" });
  const chart = screen.getByRole("heading", { name: /Runs over time/ });
  expect(heading.compareDocumentPosition(chart) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(screen.queryByText("Working set")).not.toBeInTheDocument();
});

test("the KPI cards link to the lists they count", async () => {
  renderOverview();
  const running = await screen.findByRole("link", { name: /Running now/ });
  expect(running).toHaveAttribute("href", "/runs?state=IN_PROGRESS");
  expect(screen.getByRole("link", { name: /Unresolved dead letters/ })).toHaveAttribute(
    "href",
    "/runs/dead-letters?status=AWAITING_INTERVENTION",
  );
});
