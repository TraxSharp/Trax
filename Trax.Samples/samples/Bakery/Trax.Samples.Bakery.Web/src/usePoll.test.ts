import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { poll } from "./usePoll";

// A read can take longer than the interval when the host stalls. The page shows the newest answer, so a read waits
// for the one before it to settle rather than racing it.

beforeEach(() => vi.useFakeTimers());

afterEach(() => vi.useRealTimers());

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

describe("polling", () => {
  it("starts no read while the last one is pending", async () => {
    const first = deferred<string>();
    const read = vi.fn(() => first.promise);
    const stop = poll(read, () => {}, () => {}, 1000);

    await vi.advanceTimersByTimeAsync(5000);
    expect(read).toHaveBeenCalledTimes(1);

    first.resolve("Placed");
    await vi.advanceTimersByTimeAsync(1000);
    expect(read).toHaveBeenCalledTimes(2);
    stop();
  });

  it("never shows an older answer after a newer one", async () => {
    const answers = [deferred<string>(), deferred<string>()];
    let call = 0;
    const seen: string[] = [];
    const stop = poll(() => answers[Math.min(call++, 1)].promise, (v) => seen.push(v), () => {}, 1000);

    await vi.advanceTimersByTimeAsync(1000);
    answers[1].resolve("Baking");
    await vi.advanceTimersByTimeAsync(0);
    answers[0].resolve("Placed");
    await vi.advanceTimersByTimeAsync(1000);

    expect(seen).toEqual(["Placed", "Baking"]);
    stop();
  });

  it("reads nothing more once stopped", async () => {
    const read = vi.fn(() => Promise.resolve("Placed"));
    const stop = poll(read, () => {}, () => {}, 1000);
    await vi.advanceTimersByTimeAsync(0);
    stop();

    await vi.advanceTimersByTimeAsync(5000);
    expect(read).toHaveBeenCalledTimes(1);
  });
});
