import { render, waitFor, act } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { Provider } from "urql";
import { createMockClient, createMockStore } from "../mock";
import { useRefetchOnChange, type ChangeDomain } from "./useRefetchOnChange";

function Probe({
  domain,
  onRefetch,
}: {
  domain: ChangeDomain | ChangeDomain[];
  onRefetch: () => void;
}) {
  useRefetchOnChange(domain, onRefetch);
  return null;
}

// Let urql establish the subscription source before the one-shot store event, so the event isn't
// published into the void. A microtask yield, not a timing-dependent sleep.
async function flushSubscription() {
  await act(async () => {
    await new Promise((r) => setTimeout(r, 0));
  });
}

function emit(store: ReturnType<typeof createMockStore>, domain: ChangeDomain, seq = 0) {
  store.publishEvent("OnDataChanged", {
    onDataChanged: { domain, timestamp: new Date(seq).toISOString() },
  });
}

function renderProbe(domain: ChangeDomain | ChangeDomain[]) {
  const store = createMockStore();
  const client = createMockClient({ store });
  const refetch = vi.fn();
  render(
    <Provider value={client}>
      <Probe domain={domain} onRefetch={refetch} />
    </Provider>,
  );
  return { store, refetch };
}

test("refetches when a matching domain changes", async () => {
  const { store, refetch } = renderProbe("WORK_QUEUE");
  await flushSubscription();

  emit(store, "WORK_QUEUE");

  await waitFor(() => expect(refetch).toHaveBeenCalledTimes(1));
});

test("ignores a non-matching domain", async () => {
  const { store, refetch } = renderProbe("WORK_QUEUE");
  await flushSubscription();

  emit(store, "MANIFEST");

  // A non-matching event must never refetch. Wait past the debounce window to prove the negative.
  await new Promise((r) => setTimeout(r, 400));
  expect(refetch).not.toHaveBeenCalled();
});

test("coalesces a burst into a single refetch", async () => {
  const { store, refetch } = renderProbe("DEAD_LETTER");
  await flushSubscription();

  for (let i = 0; i < 5; i++) emit(store, "DEAD_LETTER", i);

  await waitFor(() => expect(refetch).toHaveBeenCalledTimes(1));

  // The burst has settled; no further refetch should fire.
  await new Promise((r) => setTimeout(r, 400));
  expect(refetch).toHaveBeenCalledTimes(1);
});

test("matches any domain in an array", async () => {
  const { store, refetch } = renderProbe(["WORK_QUEUE", "DEAD_LETTER"]);
  await flushSubscription();

  emit(store, "DEAD_LETTER");

  await waitFor(() => expect(refetch).toHaveBeenCalledTimes(1));
});
