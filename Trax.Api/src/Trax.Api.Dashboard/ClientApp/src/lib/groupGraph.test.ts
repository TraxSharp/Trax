import { describe, expect, test } from "vitest";
import { groupGraph, groupNodeState } from "./groupGraph";

describe("a group's node in the dependency graph", () => {
  test("is toned failing first, then running, then enabled or disabled, and says so in words", () => {
    const status = (inProgress: number, lastEndedRunFailed: boolean) => ({ groupId: 1, inProgress, lastEndedRunFailed });
    expect(groupNodeState(true, status(2, true))).toEqual({ tone: "danger", detail: "Last run failed · 2 running" });
    expect(groupNodeState(true, status(2, false))).toEqual({ tone: "info", detail: "2 running" });
    expect(groupNodeState(false, status(0, false))).toEqual({ tone: "idle", detail: "Disabled" });
    expect(groupNodeState(false, status(0, true))).toEqual({ tone: "danger", detail: "Disabled · Last run failed" });
    expect(groupNodeState(true, status(0, false))).toEqual({ tone: "ok", detail: "Enabled" });
  });

  test("is drawn plain when the answer has neither its switch nor its status", () => {
    const drawn = groupGraph({ nodes: [{ id: 3, name: "ingest", isHighlighted: false }], edges: [] });
    expect(drawn.nodes).toEqual([{ id: "3", label: "ingest", tone: "idle", detail: undefined }]);
  });
});
