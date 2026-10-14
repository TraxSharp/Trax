import { describe, expect, test } from "vitest";
import {
  DEFAULT_OVERVIEW_PANELS,
  getOverviewPanels,
  resetOverviewPanels,
  setOverviewPanel,
} from "./overviewPanels";
import { getRefreshStatus, noteQueryResult, noteRefresh, resetRefreshStatus } from "./refreshStatus";
import { environmentBadgeColor } from "./environment";
import { persistedOperationPath, tenantLabel } from "./persistedOperations";
import { formatMs } from "./format";

describe("overview panels", () => {
  test("hiding a panel persists, and reset shows every panel", () => {
    setOverviewPanel("failures", false);
    expect(getOverviewPanels().failures).toBe(false);
    expect(JSON.parse(localStorage.getItem("trax:overview-panels")!).failures).toBe(false);
    resetOverviewPanels();
    expect(getOverviewPanels()).toEqual(DEFAULT_OVERVIEW_PANELS);
  });
});

describe("refresh status", () => {
  test("a failed query marks the refresh failed until one succeeds", () => {
    noteQueryResult("boom");
    expect(getRefreshStatus().lastError).toBe("boom");
    noteQueryResult(null);
    expect(getRefreshStatus().lastError).toBeNull();
  });
  test("the poll notes when it fired", () => {
    noteRefresh(1234);
    expect(getRefreshStatus().lastRefreshAt).toBe(1234);
    resetRefreshStatus();
    expect(getRefreshStatus().lastRefreshAt).toBeNull();
  });
});

describe("display helpers", () => {
  test("environment badge colours match the Blazor header", () => {
    expect(environmentBadgeColor("Development")).toBe("#1976D2");
    expect(environmentBadgeColor("production")).toBe("#D32F2F");
    expect(environmentBadgeColor("Staging")).toBe("#757575");
  });
  test("persisted operation routes carry the tenant only when there is one", () => {
    expect(persistedOperationPath("greet.v1", null)).toBe("/persisted-operations/greet.v1");
    expect(persistedOperationPath("a b", "acme")).toBe("/persisted-operations/a%20b?tenant=acme");
    expect(tenantLabel(null)).toBe("(default)");
  });
  test("durations read at the right scale", () => {
    expect(formatMs(850)).toBe("850 ms");
    expect(formatMs(12_400)).toBe("12.4 s");
    expect(formatMs(185_000)).toBe("3m 05s");
    expect(formatMs(7_620_000)).toBe("2h 07m");
  });
});
