import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import {
  getHideAdminTrains,
  setHideAdminTrains,
  useHideAdminTrains,
} from "./adminTrains";

describe("adminTrains", () => {
  beforeEach(() => setHideAdminTrains(true));
  afterEach(() => setHideAdminTrains(true));

  test("defaults to hiding admin trains", () => {
    expect(getHideAdminTrains()).toBe(true);
  });

  test("set/get round-trips", () => {
    setHideAdminTrains(false);
    expect(getHideAdminTrains()).toBe(false);
    setHideAdminTrains(true);
    expect(getHideAdminTrains()).toBe(true);
  });

  test("useHideAdminTrains reacts to setHideAdminTrains", () => {
    const { result } = renderHook(() => useHideAdminTrains());
    expect(result.current).toBe(true);
    act(() => setHideAdminTrains(false));
    expect(result.current).toBe(false);
    act(() => setHideAdminTrains(true));
    expect(result.current).toBe(true);
  });
});
