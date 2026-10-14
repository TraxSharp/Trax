import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";
import { setConnectionStatus } from "./connection";
import { useRefetchOnReconnect } from "./useRefetchOnReconnect";

describe("useRefetchOnReconnect", () => {
  beforeEach(() => setConnectionStatus("closed"));
  afterEach(() => setConnectionStatus("closed"));

  test("does not fire on the initial connect", () => {
    const refetch = vi.fn();
    renderHook(() => useRefetchOnReconnect(refetch));
    act(() => setConnectionStatus("connecting"));
    act(() => setConnectionStatus("connected"));
    expect(refetch).not.toHaveBeenCalled();
  });

  test("fires once when the socket recovers after a drop", () => {
    const refetch = vi.fn();
    renderHook(() => useRefetchOnReconnect(refetch));
    act(() => setConnectionStatus("connected")); // initial
    act(() => setConnectionStatus("closed")); // drop
    expect(refetch).not.toHaveBeenCalled();
    act(() => setConnectionStatus("connected")); // recover
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  test("fires again on each subsequent recovery", () => {
    const refetch = vi.fn();
    renderHook(() => useRefetchOnReconnect(refetch));
    act(() => setConnectionStatus("connected"));
    act(() => setConnectionStatus("connecting")); // flap
    act(() => setConnectionStatus("connected"));
    act(() => setConnectionStatus("closed"));
    act(() => setConnectionStatus("connected"));
    expect(refetch).toHaveBeenCalledTimes(2);
  });

  test("does not fire when the socket drops", () => {
    const refetch = vi.fn();
    renderHook(() => useRefetchOnReconnect(refetch));
    act(() => setConnectionStatus("connected"));
    act(() => setConnectionStatus("closed"));
    expect(refetch).not.toHaveBeenCalled();
  });

  test("uses the latest refetch closure", () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(({ fn }) => useRefetchOnReconnect(fn), {
      initialProps: { fn: first },
    });
    act(() => setConnectionStatus("connected"));
    rerender({ fn: second });
    act(() => setConnectionStatus("closed"));
    act(() => setConnectionStatus("connected"));
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });

  test("recovers when the hook mounts while already connected", () => {
    setConnectionStatus("connected");
    const refetch = vi.fn();
    renderHook(() => useRefetchOnReconnect(refetch));
    act(() => setConnectionStatus("closed"));
    act(() => setConnectionStatus("connected"));
    expect(refetch).toHaveBeenCalledTimes(1);
  });
});
