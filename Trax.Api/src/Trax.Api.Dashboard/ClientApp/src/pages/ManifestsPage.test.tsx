import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { utcText } from "../lib/schedules";

afterEach(cleanup);

const NIGHTLY = {
  id: 901,
  externalId: "nightly-import",
  name: "Trax.Demo.Trains.INightlyImportTrain",
  isEnabled: true,
  scheduleType: "CRON",
  cronExpression: "0 2 * * *",
  intervalSeconds: null,
  maxRetries: 3,
  timeoutSeconds: null,
  lastSuccessfulRun: null,
  manifestGroupId: 1,
  dependsOnManifestId: null,
  priority: 0,
  replayDecisionsOnRetry: true,
};

const NEXT = ["2099-07-08T02:00:00.000Z", "2099-07-09T02:00:00.000Z", "2099-07-10T02:00:00.000Z"];

function renderManifests() {
  const client = createMockClient({
    store: createMockStore(),
    fixtures: false,
    overrides: {
      resolvers: () => ({
        ManifestSummary: {
          scheduleText: () => "Daily at 02:00 UTC",
          nextRuns: (_row: unknown, args: { count: number }) => NEXT.slice(0, args.count),
        },
        OperationsQueries: {
          manifests: () => ({
            items: [NIGHTLY],
            totalCount: 1,
            isEstimatedCount: false,
            skip: 0,
            take: 25,
            nextCursor: null,
          }),
        },
      }),
    },
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/schedules"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
}

test("a manifest on 0 2 * * * reads Daily at 02:00 UTC, with its next run", async () => {
  renderManifests();
  const row = (await screen.findByRole("checkbox", { name: "Select #901" })).closest("tr")!;

  expect(await within(row).findByText("Daily at 02:00 UTC")).toBeInTheDocument();
  expect(within(row).queryByText("0 2 * * *")).toBeNull();
  expect(within(row).getByText(utcText(NEXT[0]))).toBeInTheDocument();
  expect(within(row).getByText(/^in \d+ days$/)).toBeInTheDocument();
});

test("Manifests and Manifest groups are two tabs of Schedules", async () => {
  renderManifests();
  const tabs = within(await screen.findByRole("tablist", { name: "Schedules" }));
  expect(tabs.getByRole("tab", { name: "Manifests" })).toHaveAttribute("aria-selected", "true");
  expect(tabs.getByRole("tab", { name: "Manifest groups" })).toHaveAttribute("href", "/schedules/groups");
});
