import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import type { ChangeDomain } from "../lib/useRefetchOnChange";

afterEach(cleanup);

type Row = { id: string; rowId: number; state: string; overdue: boolean };

const instance = (r: Row) => ({
  machine: "parcel",
  ownerKind: "SYSTEM",
  id: r.id,
  rowId: r.rowId,
  state: r.state,
  version: 1,
  createdAt: "2026-07-07T10:00:00.000Z",
  updatedAt: "2026-07-07T11:00:00.000Z",
  inStateSince: "2026-07-07T11:00:00.000Z",
  hasLiveInvokedRun: false,
  dueAt: r.overdue ? "2026-07-07T11:30:00.000Z" : null,
  overdue: r.overdue,
  stuck: r.overdue,
  waitingOn: null,
});

const PARCEL = {
  name: "parcel",
  version: 1,
  systemOwned: true,
  schemaHash: null,
  initialState: "Waiting",
  graphUnavailableReason: null,
  states: [
    { name: "Waiting", isInitial: true, isFinal: false, isCommitted: false, invokesTrain: null, spawnsMachine: null },
    { name: "Sent", isInitial: false, isFinal: true, isCommitted: false, invokesTrain: null, spawnsMachine: null },
  ],
  transitions: [
    { from: "Waiting", command: "Send", to: "Sent", kind: "COMMAND", guard: null, effect: null, after: null },
    { from: "Waiting", command: "Waiting.after", to: "Sent", kind: "TIMER", guard: null, effect: null, after: "PT1H" },
  ],
  timers: [{ state: "Waiting", command: "Waiting.after", target: "Sent", after: "PT1H", field: null, min: null, max: null }],
};

// A host whose `parcel` machine holds `rows`; `rows` can change under the page.
function host(initial: Row[], route = "/machines/parcel") {
  let rows = initial;
  let reads = 0;
  const store = createMockStore();
  const counts = () => {
    const by = new Map<string, { machine: string; state: string; ownerKind: string; count: number; stuck: number }>();
    for (const r of rows) {
      const c = by.get(r.state) ?? { machine: "parcel", state: r.state, ownerKind: "SYSTEM", count: 0, stuck: 0 };
      c.count++;
      if (r.overdue) c.stuck++;
      by.set(r.state, c);
    }
    return [...by.values()];
  };
  const client = createMockClient({
    store,
    fixtures: false,
    overrides: {
      resolvers: () => ({
        OperationsQueries: {
          machines: () => [PARCEL],
          machineInstanceCounts: () => counts(),
          machineInstances: (_root: unknown, args: Record<string, unknown>) => {
            reads += 1;
            const matching = rows.filter(
              (r) => (args.state == null || r.state === args.state) && (args.stuck == null || r.overdue === args.stuck),
            );
            const skip = Number(args.skip);
            const take = Number(args.take);
            return {
              items: matching.slice(skip, skip + take).map(instance),
              totalCount: matching.length,
              isCountCapped: false,
            };
          },
        },
      }),
    },
  });
  let location = "";
  function Where() {
    const l = useLocation();
    location = `${l.pathname}${l.search}`;
    return null;
  }
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
        <Where />
      </MemoryRouter>
    </Provider>,
  );
  return {
    set: (next: Row[]) => {
      rows = next;
    },
    reads: () => reads,
    location: () => location,
    signal: async (domain: ChangeDomain) => {
      await new Promise((r) => setTimeout(r, 0));
      store.publishEvent("OnDataChanged", { onDataChanged: { domain, timestamp: new Date(0).toISOString() } });
    },
  };
}

const ids = (n: number) => `3f2c1a00-0000-4000-8000-${String(n).padStart(12, "0")}`;
const row = (n: number, state: string, overdue = false): Row => ({ id: ids(n), rowId: n, state, overdue });

const listed = () =>
  within(screen.getByRole("region", { name: "Instances" }))
    .getAllByRole("row")
    .slice(1)
    .map((r) => r.getAttribute("data-instance-id"))
    .filter((id) => id != null);

test("each state shows how many instances are in it and how many are stuck, and clicking it lists them", async () => {
  const h = host([row(1, "Waiting", true), row(2, "Waiting"), row(3, "Sent")]);
  const waiting = await screen.findByRole("button", { name: /^Waiting/ });
  await waitFor(() => expect(within(waiting).getByLabelText("Instances: 2")).toBeInTheDocument());
  expect(within(waiting).getByLabelText("Stuck: 1")).toBeInTheDocument();
  const sent = screen.getByRole("button", { name: /^Sent/ });
  expect(within(sent).getByLabelText("Instances: 1")).toBeInTheDocument();
  expect(within(sent).queryByLabelText(/^Stuck/)).not.toBeInTheDocument();
  await waitFor(() => expect(listed()).toHaveLength(3));

  fireEvent.click(waiting);
  await waitFor(() => expect(listed()).toEqual([ids(1), ids(2)]));
  expect(h.location()).toBe("/machines/parcel?state=Waiting");
  expect(waiting).toHaveAttribute("aria-pressed", "true");

  // Clicking it again lists them all.
  fireEvent.click(screen.getByRole("button", { name: /^Waiting/ }));
  await waitFor(() => expect(listed()).toHaveLength(3));
  expect(h.location()).toBe("/machines/parcel");
});

test("the stuck filter lists only the stuck instances, and is held in the URL", async () => {
  const h = host([row(1, "Waiting", true), row(2, "Waiting"), row(3, "Sent")]);
  await waitFor(() => expect(listed()).toHaveLength(3));
  fireEvent.click(screen.getByRole("checkbox", { name: "Stuck only" }));
  await waitFor(() => expect(listed()).toEqual([ids(1)]));
  expect(h.location()).toBe("/machines/parcel?stuck=true");
  const r = within(screen.getByRole("region", { name: "Instances" })).getAllByRole("row")[1];
  expect(within(r).getByText("Overdue")).toBeInTheDocument();
  expect(within(r).getByText("Stuck")).toBeInTheDocument();
});

test("a timer is drawn dashed and labelled with its delay, a command with its name", async () => {
  host([]);
  expect(await screen.findByText("after 1 hour")).toBeInTheDocument();
  expect(screen.getByText("Send")).toBeInTheDocument();
  expect(await screen.findByText("parcel has no instances. An instance is listed here once something starts one.")).toBeInTheDocument();
});

test("the list is read again when runs or the work queue change", async () => {
  const h = host([row(1, "Waiting"), row(2, "Waiting")]);
  await waitFor(() => expect(listed()).toHaveLength(2));
  const before = h.reads();
  await h.signal("EXECUTION");
  await waitFor(() => expect(h.reads()).toBeGreaterThan(before));
  const mid = h.reads();
  await h.signal("WORK_QUEUE");
  await waitFor(() => expect(h.reads()).toBeGreaterThan(mid));
});

test("a page that empties under the operator moves back to the last page that has rows", async () => {
  const many = (n: number) => Array.from({ length: n }, (_, i) => row(i + 1, "Waiting"));
  const h = host(many(25));
  expect(await screen.findByText("1–20 of 25")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  expect(await screen.findByText("21–25 of 25")).toBeInTheDocument();
  h.set(many(20));
  await h.signal("EXECUTION");
  expect(await screen.findByText("1–20 of 20")).toBeInTheDocument();
  expect(screen.queryByText(/^21–/)).not.toBeInTheDocument();
  expect(screen.queryByText("No instances match.")).not.toBeInTheDocument();
});

test("the machines list links each machine and its stuck instances", async () => {
  host([row(1, "Waiting", true)], "/machines");
  const list = await screen.findByRole("region", { name: "Registered machines" });
  const link = await within(list).findByRole("link", { name: "parcel" });
  expect(link).toHaveAttribute("href", "/machines/parcel");
  await waitFor(() => expect(within(list).getByRole("link", { name: "1 stuck" })).toHaveAttribute("href", "/machines/parcel?stuck=true"));
});
