import { expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import { devMockSeed } from "./seeds";
import { MACHINES, MACHINE_INSTANCES_ATTENTION, MACHINE_INSTANCE_STUCK_COUNTS } from "../graphql/queries";

// The machine reads the console's machine pages will make, answered in mock mode.

function client() {
  return createMockClient({ store: createMockStore(), fixtures: false, overrides: devMockSeed });
}

test("mock mode lists every machine with its graph, one with no instance included", async () => {
  const result = await client().query(MACHINES, {}).toPromise();
  expect(result.error).toBeUndefined();
  const machines = (result.data as { operations: { machines: { name: string; states: unknown[]; transitions: { kind: string }[] }[] } })
    .operations.machines;
  expect(machines.map((m) => m.name)).toEqual(["retention-sweep", "source-partition", "topic-map"]);
  const topicMap = machines.find((m) => m.name === "topic-map")!;
  expect(topicMap.states.length).toBeGreaterThan(0);
  expect(new Set(topicMap.transitions.map((t) => t.kind))).toEqual(new Set(["COMMAND", "TIMER", "OUTCOME"]));
});

test("mock mode marks the overdue instance stuck, lists it under stuck: true and counts it", async () => {
  const c = client();
  const stuck = await c.query(MACHINE_INSTANCES_ATTENTION, { stuck: true, skip: 0, take: 25 }).toPromise();
  expect(stuck.error).toBeUndefined();
  const items = (stuck.data as { operations: { machineInstances: { items: { state: string; overdue: boolean; stuck: boolean; inStateSince: string; updatedAt: string }[] } } })
    .operations.machineInstances.items;
  expect(items).toHaveLength(1);
  expect(items[0]).toMatchObject({ state: "ChoosingRange", overdue: true, stuck: true });
  expect(items[0].inStateSince).toBe(items[0].updatedAt);

  const all = await c.query(MACHINE_INSTANCES_ATTENTION, { skip: 0, take: 25 }).toPromise();
  const waiting = (all.data as { operations: { machineInstances: { items: { waitingOn: { trainState: string } | null }[] } } })
    .operations.machineInstances.items.filter((i) => i.waitingOn != null);
  expect(waiting.map((i) => i.waitingOn!.trainState)).toContain("PENDING");

  const counts = await c.query(MACHINE_INSTANCE_STUCK_COUNTS, {}).toPromise();
  expect(counts.error).toBeUndefined();
  const rows = (counts.data as { operations: { machineInstanceCounts: { state: string; stuck: number }[] } }).operations.machineInstanceCounts;
  expect(rows.find((r) => r.state === "ChoosingRange")?.stuck).toBe(1);
  expect(rows.find((r) => r.state === "Ingesting")?.stuck).toBe(0);
});
