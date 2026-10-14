import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { getPollSeconds, setPollSeconds, usePoll, usePollSeconds } from "./poll";

describe("poll", () => {
  beforeEach(() => setPollSeconds(0));
  afterEach(() => {
    vi.useRealTimers();
    setPollSeconds(0);
  });

  test("set/get round-trips, floors, and clamps negatives to 0", () => {
    setPollSeconds(15);
    expect(getPollSeconds()).toBe(15);
    setPollSeconds(3.9);
    expect(getPollSeconds()).toBe(3); // floored
    setPollSeconds(-5);
    expect(getPollSeconds()).toBe(0); // clamped
  });

  test("usePollSeconds reacts to setPollSeconds", () => {
    const { result } = renderHook(() => usePollSeconds());
    expect(result.current).toBe(0);
    act(() => setPollSeconds(20));
    expect(result.current).toBe(20);
  });

  test("usePoll fires refetch on the interval when enabled", () => {
    vi.useFakeTimers();
    const refetch = vi.fn();
    setPollSeconds(1);
    renderHook(() => usePoll(refetch));

    expect(refetch).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(1000));
    expect(refetch).toHaveBeenCalledTimes(1);
    act(() => vi.advanceTimersByTime(2000));
    expect(refetch).toHaveBeenCalledTimes(3);
  });

  test("usePoll is a no-op when the interval is 0", () => {
    vi.useFakeTimers();
    const refetch = vi.fn();
    setPollSeconds(0);
    renderHook(() => usePoll(refetch));
    act(() => vi.advanceTimersByTime(10_000));
    expect(refetch).not.toHaveBeenCalled();
  });

  test("usePoll always calls the latest refetch without resetting the interval", () => {
    vi.useFakeTimers();
    const first = vi.fn();
    const second = vi.fn();
    setPollSeconds(1);
    const { rerender } = renderHook(({ fn }) => usePoll(fn), {
      initialProps: { fn: first },
    });
    act(() => vi.advanceTimersByTime(1000));
    expect(first).toHaveBeenCalledTimes(1);

    rerender({ fn: second });
    act(() => vi.advanceTimersByTime(1000));
    expect(second).toHaveBeenCalledTimes(1); // latest closure
    expect(first).toHaveBeenCalledTimes(1); // no extra call to the stale one
  });
});
