import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ConsoleClientProvider } from "../lib/consoleClient";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";

afterEach(cleanup);

// The header's Drained mark on a page with no drain panel: a drain the console did not set (a
// deploy's) arrives as a scheduler change, and one that lapses sends nothing at all.
function open(drain: () => { drained: boolean; until: string | null }) {
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        ConfigQueries: {
          drain: () => ({
            ...drain(),
            startedAt: null,
            startedBy: "deploy",
            busy: [],
            busyTotal: 0,
          }),
        },
      }),
    },
  });
  render(
    <ConsoleClientProvider client={client}>
      <MemoryRouter initialEntries={["/runs/queued"]}>
        <AppRoutes />
      </MemoryRouter>
    </ConsoleClientProvider>,
  );
  return store;
}

const header = () => within(screen.getByRole("banner"));

test("a drain set elsewhere shows once the scheduler change signal arrives", async () => {
  let drained = false;
  const store = open(() => ({ drained, until: drained ? new Date(Date.now() + 600_000).toISOString() : null }));
  await screen.findByRole("banner");
  await new Promise((r) => setTimeout(r, 50));
  expect(header().queryByRole("link", { name: "Drained" })).not.toBeInTheDocument();

  drained = true;
  store.publishEvent("OnDataChanged", {
    onDataChanged: { domain: "SCHEDULER_CONFIG", timestamp: new Date(0).toISOString() },
  });

  expect(await header().findByRole("link", { name: "Drained" })).toHaveAttribute("href", "/system/scheduler");
});

test("a drain that lapses drops the mark at its until, with no signal", async () => {
  const lapses = Date.now() + 300;
  open(() => {
    const now = Date.now();
    return { drained: now < lapses, until: now < lapses ? new Date(lapses).toISOString() : null };
  });
  expect(await header().findByRole("link", { name: "Drained" })).toBeInTheDocument();

  await waitFor(() => expect(header().queryByRole("link", { name: "Drained" })).not.toBeInTheDocument(), {
    timeout: 5000,
  });
});
