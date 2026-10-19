import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { MACHINE_IDS, machineQueryResolvers } from "../mock/scenarios";

afterEach(cleanup);

// The idle source-partition instance, in Ingested; the host's read of it can move under the page.
function host() {
  let state = "Ingested";
  let updatedAt = "2026-07-07T11:54:00.000Z";
  let reads = 0;
  const store = createMockStore();
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        OperationsQueries: {
          ...machineQueryResolvers,
          machineInstance: (root: unknown, args: Record<string, unknown>) => {
            reads += 1;
            const detail = machineQueryResolvers.machineInstance(root, args);
            return detail && { ...detail, state, updatedAt, inStateSince: updatedAt };
          },
        },
      }),
    },
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[`/machines/source-partition/system/${MACHINE_IDS.idle}`]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  const push = async (change: Partial<{ id: string; state: string; updatedAt: string; deleted: boolean; reload: boolean }>) => {
    await act(async () => {
      store.publishEvent("OnMachineInstanceChanged", {
        onMachineInstanceChanged: {
          rowId: 13,
          machine: "source-partition",
          ownerKind: "SYSTEM",
          id: MACHINE_IDS.idle,
          state,
          updatedAt,
          hasLiveInvokedRun: false,
          revision: 2,
          deleted: false,
          reload: false,
          ...change,
        },
      });
      await new Promise((r) => setTimeout(r, 0));
    });
  };
  return {
    push,
    reads: () => reads,
    // The host writes the instance: what a read returns from now on.
    write: (next: string, at: string) => {
      state = next;
      updatedAt = at;
    },
  };
}

const lit = () =>
  within(screen.getByRole("group", { name: "States of source-partition" }))
    .getAllByText((_, el) => el?.getAttribute("aria-current") === "step")
    .map((el) => el.firstChild?.textContent);

test("the feed moves the lit state as soon as it says the instance moved, and the page reads it again", async () => {
  const h = host();
  await waitFor(() => expect(lit()).toEqual(["Ingested"]));
  const before = h.reads();

  // The feed is ahead of the read: the lit node moves on the event alone.
  await h.push({ state: "Failed", updatedAt: "2026-07-07T12:00:00.000Z" });
  await waitFor(() => expect(lit()).toEqual(["Failed"]));
  expect(within(screen.getByRole("list", { name: "Commands it accepts" })).getByText("Retry")).toBeInTheDocument();
  await waitFor(() => expect(h.reads()).toBeGreaterThan(before));
});

test("another instance's write changes nothing; a reload reads it again", async () => {
  const h = host();
  await waitFor(() => expect(lit()).toEqual(["Ingested"]));
  const before = h.reads();
  await h.push({ id: MACHINE_IDS.dispatched, state: "Failed", updatedAt: "2026-07-07T12:00:00.000Z" });
  expect(lit()).toEqual(["Ingested"]);
  expect(h.reads()).toBe(before);

  h.write("Pending", "2026-07-07T12:05:00.000Z");
  await h.push({ id: "00000000-0000-0000-0000-000000000000", reload: true });
  await waitFor(() => expect(lit()).toEqual(["Pending"]));
});

test("an instance deleted under the page says so", async () => {
  const h = host();
  await waitFor(() => expect(lit()).toEqual(["Ingested"]));
  await h.push({ deleted: true, updatedAt: "2026-07-07T12:00:00.000Z" });
  expect(await screen.findByText(/This instance was deleted or has expired/)).toBeInTheDocument();
});
