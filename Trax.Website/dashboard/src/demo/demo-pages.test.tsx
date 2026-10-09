import { cleanup, fireEvent, within } from "@testing-library/react";
import { afterEach, describe, expect, test } from "vitest";
import { setHideAdminTrains } from "../lib/adminTrains";
import { DISABLE_MANIFEST, TRIGGER_MANIFEST } from "../graphql/mutations";
import { MANIFESTS } from "../graphql/queries";
import { createMockStore } from "../mock/store/mock-store";
import { defaultOverlays } from "../mock/store/overlays";
import { createDemoClient } from "./client";
import {
  everyPage,
  everySelect,
  newLedger,
  notOfferedYet,
  pageOf,
  recordedIds,
  recordings,
  renderDemo,
  served,
  settle,
  type Ledger,
} from "./harness";

// The demo's strict check: a visitor who opens every page, picks every option of every filter and
// sort, pages every list to its end and opens every row is answered from a recording every time,
// never by a fallback. A miss here means the recorder (Trax.Samples scripts/recordings/dashboard.mjs)
// needs to record more.

afterEach(cleanup);

function expectAllRecorded(ledger: Ledger) {
  const misses = [...served(ledger, "missing"), ...served(ledger, "derived")].map(
    (e) => `${e.served} ${e.op} ${JSON.stringify(e.variables)} (on ${e.where})`,
  );
  expect(misses).toEqual([]);
  expect(served(ledger, "recorded").length).toBeGreaterThan(0);
}

const LIST_PAGES = [
  "/executions",
  "/work-queue",
  "/dead-letters",
  "/manifests",
  "/groups",
  "/logs",
  "/persisted-operations",
  // Offered once the recordings hold it (harness.tsx, NOT_RECORDED_YET).
  ...(notOfferedYet("MachineInstances") ? [] : ["/state-machines"]),
];
const OTHER_PAGES = ["/", "/trains", "/cluster", "/realtime", "/settings/user", "/settings/server", "/settings/effects"];

describe("every page, filter, sort and page of every list", () => {
  for (const hideAdmin of [true, false]) {
    for (const route of LIST_PAGES) {
      test(`${route} (admin trains ${hideAdmin ? "hidden" : "shown"})`, async () => {
        setHideAdminTrains(hideAdmin);
        const ledger = newLedger();
        const { container } = renderDemo(route, ledger);
        await settle();
        const page = pageOf(container);
        await everyPage(page);
        await everySelect(page);
        expectAllRecorded(ledger);
      });
    }
    for (const route of OTHER_PAGES) {
      test(`${route} (admin trains ${hideAdmin ? "hidden" : "shown"})`, async () => {
        setHideAdminTrains(hideAdmin);
        const ledger = newLedger();
        const { container } = renderDemo(route, ledger);
        await settle();
        await everySelect(pageOf(container));
        expectAllRecorded(ledger);
      });
    }
  }
});

describe("every row's page", () => {
  const details: [string, string, string][] = [
    ["ExecutionDetail", "id", "/executions/"],
    ["WorkQueueDetail", "id", "/work-queue/"],
    ["DeadLetterDetail", "id", "/dead-letters/"],
    ["ManifestDetail", "id", "/manifests/"],
    ["ManifestGroupDetail", "id", "/groups/"],
    ["TrainStats", "trainName", "/trains/"],
  ];
  for (const [op, variable, prefix] of details) {
    test(`${prefix}:id for every recorded ${op}`, async () => {
      const ledger = newLedger();
      const ids = recordedIds(op, variable);
      expect(ids.length).toBeGreaterThan(0);
      for (const id of ids) {
        const { container, unmount } = renderDemo(`${prefix}${encodeURIComponent(String(id))}`, ledger);
        await settle();
        await everyPage(pageOf(container));
        unmount();
      }
      expectAllRecorded(ledger);
    }, 240_000);
  }

  test("/persisted-operations/:id for every operation in the store", async () => {
    const ledger = newLedger();
    const keys = Object.keys(recordings().queries.PersistedOperationDetail ?? {}).map(
      (k) => JSON.parse(k) as { id: string; tenantKey?: string },
    );
    expect(keys.length).toBeGreaterThan(0);
    for (const { id, tenantKey } of keys) {
      const query = tenantKey != null ? `?tenant=${encodeURIComponent(tenantKey)}` : "";
      const { unmount } = renderDemo(`/persisted-operations/${encodeURIComponent(id)}${query}`, ledger);
      await settle();
      unmount();
    }
    expectAllRecorded(ledger);
  });
});

describe("every link leads to a recorded page", () => {
  // Follows every link on every page, from the sidebar's pages and every recorded row's page, so a
  // reference one host's row makes (a dead letter's retry, a replay's source, a run's manifest)
  // points at a row the recordings have.
  test("crawl", async () => {
    const ledger = newLedger();
    const queue = [
      ...LIST_PAGES,
      ...OTHER_PAGES,
      ...recordedIds("ExecutionDetail", "id").map((id) => `/executions/${id}`),
      ...recordedIds("WorkQueueDetail", "id").map((id) => `/work-queue/${id}`),
      ...recordedIds("DeadLetterDetail", "id").map((id) => `/dead-letters/${id}`),
      ...recordedIds("ManifestDetail", "id").map((id) => `/manifests/${id}`),
      ...recordedIds("ManifestGroupDetail", "id").map((id) => `/groups/${id}`),
    ];
    const seen = new Set(queue);
    while (queue.length > 0) {
      const route = queue.shift()!;
      const { container, unmount } = renderDemo(route, ledger);
      await settle();
      for (const a of container.querySelectorAll("a[href]")) {
        const href = a.getAttribute("href")!;
        if (!href.startsWith("/") || seen.has(href)) continue;
        seen.add(href);
        queue.push(href);
      }
      unmount();
    }
    expect(seen.size).toBeGreaterThan(100);
    expectAllRecorded(ledger);
  }, 600_000);
});

describe("what the recordings do not hold yet", () => {
  // Nothing in the demo leads to a read no recording answers: the page parts that need one are not
  // offered (lib/answerable.ts) until the recorder records it.
  test("the sidebar offers no State machines page and a run page draws no run graph", async () => {
    if (!notOfferedYet("MachineInstances") && !notOfferedYet("RunGraph")) return;
    const ledger = newLedger();
    const { container, unmount } = renderDemo("/", ledger);
    await settle();
    expect(within(container).queryByRole("link", { name: "State machines" }) == null).toBe(
      notOfferedYet("MachineInstances"),
    );
    unmount();
    for (const id of recordedIds("ExecutionDetail", "id").slice(0, 10)) {
      const page = renderDemo(`/executions/${id}`, ledger);
      await settle();
      expect(within(page.container).queryByRole("region", { name: "Run graph" }) == null).toBe(notOfferedYet("RunGraph"));
      page.unmount();
    }
    expect(served(ledger, "missing")).toEqual([]);
  });
});

describe("writes", () => {
  test("every write the overlays handle has the host's own answer recorded", () => {
    // A write whose page part the demo does not offer yet (harness.tsx) is never sent.
    const ops = defaultOverlays.flatMap((o) => Object.keys(o.mutations ?? {})).filter((op) => !notOfferedYet(op));
    const unrecorded = ops.filter((op) => !(recordings().mutations[op]?.length > 0));
    expect(unrecorded).toEqual([]);
  });

  test("a write answers in the host's words, a refusal changes nothing, and the change reads back", async () => {
    const client = createDemoClient({
      recordings: recordings(),
      store: createMockStore({ persist: false, exposeOnWindow: false }),
    });
    const refused = await client.mutation(TRIGGER_MANIFEST, { externalId: "no-such-manifest", askAfresh: false }).toPromise();
    expect(refused.data.operations.triggerManifest).toEqual({
      success: false,
      message: "Manifest 'no-such-manifest' not found.",
    });

    const read = async () =>
      (await client.query(MANIFESTS, { take: 25, hideAdminTrains: true }, { requestPolicy: "network-only" }).toPromise())
        .data.operations.manifests.items.find((m: { externalId: string }) => m.externalId === "send-daily-digest");
    expect((await read()).isEnabled).toBe(true);
    const disabled = await client.mutation(DISABLE_MANIFEST, { externalId: "send-daily-digest" }).toPromise();
    expect(disabled.data.operations.disableManifest.message).toBe("Manifest disabled");
    expect((await read()).isEnabled).toBe(false);
  });

  test("cancelling a queued entry on the page reads back cancelled", async () => {
    const ledger = newLedger();
    const { container } = renderDemo("/work-queue", ledger);
    await settle();
    const page = pageOf(container);
    const badges = () => within(page).queryAllByText("Cancelled", { selector: "span" }).length;
    const before = badges();
    const original = window.confirm;
    window.confirm = () => true;
    try {
      const cancel = within(page).getAllByRole("button", { name: /^Cancel$/ })[0];
      fireEvent.click(cancel);
      await settle();
      expect(badges()).toBe(before + 1);
    } finally {
      window.confirm = original;
    }
    expectAllRecorded(ledger);
  });
});
