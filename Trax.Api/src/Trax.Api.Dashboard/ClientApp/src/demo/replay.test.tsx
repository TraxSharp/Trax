import { act, cleanup, render } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { Provider } from "urql";
import { NotificationBridge } from "../components/NotificationBridge";
import { setFailureAlertLimit } from "../lib/notify";
import { getToasts, setToastLimit, subscribeToasts, toast, type Toast } from "../lib/toast";
import { createMockStore } from "../mock/store/mock-store";
import { createDemoClient } from "./client";
import { recordings } from "./harness";
import { recordedAdminTrainNames, startReplay } from "./replay";

// What the demo's replayed live feed shows: the scheduler's own trains only when the visitor asks for
// them, and a failure toast for the first two failures of the page load, never one per loop.

type StateEvent = { onTrainStateChanged: { trainName: string; trainState: string } };

const frames = () => recordings().subscriptions.OnTrainStateChanged;
// One pass of the recorded feed, plus the gap before it plays again.
const loopMs = () => frames()[frames().length - 1].t - frames()[0].t + 5_000;

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  setFailureAlertLimit(Infinity);
  setToastLimit(Infinity);
});

function played(hideAdmin: boolean) {
  const store = createMockStore({ persist: false, exposeOnWindow: false });
  const names: string[] = [];
  store.subscribeEvents((op, data) => {
    if (op === "OnTrainStateChanged") names.push((data as StateEvent).onTrainStateChanged.trainName);
  });
  const stop = startReplay(store, recordings(), () => hideAdmin);
  vi.advanceTimersByTime(loopMs());
  stop();
  return names;
}

test("the recordings carry admin-train frames, so the filter has something to do", () => {
  const admin = recordedAdminTrainNames(recordings());
  expect(admin.size).toBeGreaterThan(0);
  expect(frames().some((f) => admin.has((f.data as StateEvent).onTrainStateChanged.trainName))).toBe(true);
});

test("plays no admin train's state change while admin trains are hidden", () => {
  const admin = recordedAdminTrainNames(recordings());
  const names = played(true);
  expect(names.length).toBeGreaterThan(0);
  expect(names.filter((n) => admin.has(n))).toEqual([]);
});

test("plays every frame once admin trains are shown", () => {
  const admin = recordedAdminTrainNames(recordings());
  const names = played(false);
  expect(names).toHaveLength(frames().length);
  expect(names.some((n) => admin.has(n))).toBe(true);
});

test("toasts only the first two failures of the page load, across loops of the replay", async () => {
  setFailureAlertLimit(2);
  const raised: Toast[] = [];
  const unsubscribe = subscribeToasts(() => {
    for (const t of getToasts()) if (!raised.some((r) => r.id === t.id)) raised.push(t);
  });

  const store = createMockStore({ persist: false, exposeOnWindow: false });
  render(
    <Provider value={createDemoClient({ recordings: recordings(), store })}>
      <NotificationBridge />
    </Provider>,
  );
  await act(async () => {}); // the bridge's subscription starts
  const stop = startReplay(store, recordings(), () => true);
  // Frame by frame, so each event renders as it would in a browser rather than batching into the last.
  const end = Date.now() + 3 * loopMs();
  while (Date.now() < end) act(() => void vi.advanceTimersToNextTimer());
  stop();
  unsubscribe();

  const recordedFailures = frames().filter((f) => (f.data as StateEvent).onTrainStateChanged.trainState === "FAILED");
  expect(recordedFailures.length).toBeGreaterThan(2);
  expect(raised.filter((t) => t.kind === "error")).toHaveLength(2);
  const admin = [...recordedAdminTrainNames(recordings())].map((n) => n.split(".").pop()!);
  expect(raised.filter((t) => admin.some((a) => t.message.startsWith(`${a} `)))).toEqual([]);
});

test("never stands more than the toast limit at once, dropping the oldest", () => {
  setToastLimit(2);
  toast("one");
  toast("two");
  toast("three");
  expect(getToasts().map((t) => t.message)).toEqual(["two", "three"]);
});
