import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { TRAIN_DETAIL_TRAIN, TRAIN_NEVER_RAN, trainDetailScenario } from "../mock/scenarios";
import { GRID_RUNS } from "../lib/trainChain";

afterEach(cleanup);

function renderTrain(train: string, query = "") {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: trainDetailScenario });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[`/trains/${encodeURIComponent(train)}${query}`]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
}

test("a train that never ran draws its chain and offers Run and Queue", async () => {
  renderTrain(TRAIN_NEVER_RAN);

  const chain = await screen.findByRole("group", { name: "Chain of AlphaTrain" });
  for (const step of ["Fetch", "Findings", "Summarize", "Route", "Trim", "Split", "Publish"])
    await waitFor(() => expect(within(chain).getByText(step)).toBeInTheDocument());
  await screen.findByText("This train has not run yet.");
  expect(screen.queryByRole("table", { name: "Recent runs by step" })).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Queue a run" }));
  expect(await screen.findByRole("heading", { name: "Queue a train" })).toBeInTheDocument();
});

test("the empty state's Run opens the Run dialog for the train", async () => {
  renderTrain(TRAIN_NEVER_RAN);
  fireEvent.click(await screen.findByRole("button", { name: "Run now" }));
  expect(await screen.findByRole("heading", { name: /Run a train/i })).toBeInTheDocument();
});

test("the grid has a column per recent run and a row per step, each cell linking its run", async () => {
  renderTrain(TRAIN_DETAIL_TRAIN);

  const grid = await screen.findByRole("table", { name: "Recent runs by step" });
  await waitFor(() => expect(within(grid).getByRole("link", { name: "Run #959, Summarize: Failed" })).toBeInTheDocument());
  expect(within(grid).getByRole("link", { name: "Run #959, Summarize: Failed" })).toHaveAttribute("href", "/runs/959");
  expect(within(grid).getByRole("link", { name: "Run #958, Split: Skipped" })).toBeInTheDocument();
  expect(within(grid).getByRole("link", { name: "Run #960, Summarize: Running" })).toBeInTheDocument();
  // Oldest on the left: the header row runs 951 to 960.
  const header = within(grid).getAllByRole("row")[0];
  const runs = within(header).getAllByRole("link").map((a) => a.getAttribute("href"));
  expect(runs).toHaveLength(Math.min(10, GRID_RUNS));
  expect(runs[0]).toBe("/runs/951");
  expect(runs[runs.length - 1]).toBe("/runs/960");
});

test("each step carries its failures over the range, and the rest are counted apart", async () => {
  renderTrain(TRAIN_DETAIL_TRAIN, "?range=7d");

  const chain = await screen.findByRole("group", { name: "Chain of ZebraTrain" });
  await waitFor(() =>
    expect(within(chain).getByLabelText("Failed runs in the last 7 days: 3")).toBeInTheDocument(),
  );
  expect(await screen.findByText(/1 stopped outside a step drawn here/)).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Runs over time, last 7 days" })).toBeInTheDocument();
});

test("its manifests say when they run next, and the machines that start it are linked", async () => {
  renderTrain(TRAIN_DETAIL_TRAIN);

  const manifests = await screen.findByRole("region", { name: "Manifests" });
  await waitFor(() => expect(within(manifests).getByText("Every 5 minutes")).toBeInTheDocument());
  expect(within(manifests).getByText(/Next run/)).toBeInTheDocument();
  expect(within(manifests).getByRole("link", { name: "zebra-every-5" })).toHaveAttribute("href", "/schedules/801");

  const machines = screen.getByRole("region", { name: "Started by machines" });
  await waitFor(() =>
    expect(within(machines).getByRole("link", { name: "Instances of zebra-feeder in Feeding" })).toHaveAttribute(
      "href",
      "/machines?machine=zebra-feeder&state=Feeding",
    ),
  );
});
