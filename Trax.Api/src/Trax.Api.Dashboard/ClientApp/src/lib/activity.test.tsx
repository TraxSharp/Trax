import { act, render, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, test } from "vitest";
import { isActive, resetActivity, setActiveCount, useActivity } from "./activity";
import { GlobalProgressBar } from "../components/GlobalProgressBar";

describe("activity", () => {
  afterEach(() => resetActivity());

  test("setActiveCount toggles isActive and clamps negatives", () => {
    expect(isActive()).toBe(false);
    setActiveCount(2);
    expect(isActive()).toBe(true);
    setActiveCount(0);
    expect(isActive()).toBe(false);
    setActiveCount(-1);
    expect(isActive()).toBe(false);
  });

  test("useActivity reacts to the count", () => {
    const { result } = renderHook(() => useActivity());
    expect(result.current).toBe(false);
    act(() => setActiveCount(1));
    expect(result.current).toBe(true);
    act(() => setActiveCount(0));
    expect(result.current).toBe(false);
  });

  test("GlobalProgressBar shows only while something is in flight", () => {
    const { queryByRole } = render(<GlobalProgressBar />);
    expect(queryByRole("progressbar")).not.toBeInTheDocument();
    act(() => setActiveCount(1));
    expect(queryByRole("progressbar")).toBeInTheDocument();
    act(() => setActiveCount(0));
    expect(queryByRole("progressbar")).not.toBeInTheDocument();
  });
});
