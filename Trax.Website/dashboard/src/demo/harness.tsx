import { act, fireEvent, render, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { isActive } from "../lib/activity";
import { hashVariables } from "../mock/variables-hash";
import { createMockStore } from "../mock/store/mock-store";
import { createDemoClient } from "./client";
import type { Served } from "./lookup";
import { prepareRecordings, type Recordings, type RecordingsFile } from "./recordings";
import file from "./data/dashboard-recordings.json";

// Test helpers that drive the whole demo app, as a visitor would, and keep a ledger of how every
// query it sent was answered (see lookup.ts for recorded / derived / created / missing).

let prepared: Recordings | null = null;

/** The committed recordings, prepared once per test file. */
export function recordings(): Recordings {
  prepared ??= prepareRecordings(file as unknown as RecordingsFile);
  return prepared;
}

export interface Ledger {
  entries: Map<string, { op: string; variables: Record<string, unknown>; served: Served; where: string }>;
  where: string;
}

export function newLedger(): Ledger {
  return { entries: new Map(), where: "" };
}

export function served(ledger: Ledger, kind: Served) {
  return [...ledger.entries.values()].filter((e) => e.served === kind);
}

/** Renders the demo app at `route`, every query it sends written to `ledger`. */
export function renderDemo(route: string, ledger: Ledger) {
  const store = createMockStore({ persist: false, exposeOnWindow: false });
  const client = createDemoClient({
    recordings: recordings(),
    store,
    onServe: (op, variables, how) => {
      const key = `${op} ${hashVariables(variables)}`;
      const known = ledger.entries.get(key);
      // A later "recorded" answer never hides an earlier miss.
      if (!known || known.served === "recorded") ledger.entries.set(key, { op, variables, served: how, where: ledger.where });
    },
  });
  ledger.where = route;
  return render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
}

/** Lets every query in flight answer and the page render what it got. */
export async function settle() {
  for (let i = 0; i < 3; i++) {
    await act(async () => {
      await new Promise((r) => setTimeout(r, 0));
    });
    await waitFor(() => {
      if (isActive()) throw new Error("queries in flight");
    });
  }
}

/** The page's own content, without the sidebar and header. */
export function pageOf(container: HTMLElement): HTMLElement {
  return (container.querySelector("main .max-w-6xl") as HTMLElement | null) ?? container;
}

/** Picks every option of every select on the page, one select at a time, putting each back after. */
export async function everySelect(page: HTMLElement) {
  const count = within(page).queryAllByRole("combobox").length;
  for (let i = 0; i < count; i++) {
    const select = () => within(page).queryAllByRole("combobox")[i] as HTMLSelectElement | undefined;
    const first = select();
    if (!first || first.disabled) continue;
    const original = first.value;
    const values = [...first.options].map((o) => o.value);
    for (const value of values) {
      const s = select();
      if (!s) break;
      fireEvent.change(s, { target: { value } });
      await settle();
    }
    const s = select();
    if (s) {
      fireEvent.change(s, { target: { value: original } });
      await settle();
    }
  }
}

/** Pages forward to the end with the keyset pager, then back to the start. */
export async function everyPage(page: HTMLElement) {
  const button = (name: string) => within(page).queryAllByRole("button", { name })[0] as HTMLButtonElement | undefined;
  let forward = 0;
  while (button("Next") && !button("Next")!.disabled && forward < 60) {
    fireEvent.click(button("Next")!);
    await settle();
    forward++;
  }
  while (button("Previous") && !button("Previous")!.disabled && forward-- > 0) {
    fireEvent.click(button("Previous")!);
    await settle();
  }
}

/** Every id a recorded query of `op` was answered for, by the variable that carries it. */
export function recordedIds(op: string, variable: string): unknown[] {
  return Object.keys(recordings().queries[op] ?? {})
    .map((key) => (JSON.parse(key) as Record<string, unknown>)[variable])
    .filter((v) => v !== undefined);
}
