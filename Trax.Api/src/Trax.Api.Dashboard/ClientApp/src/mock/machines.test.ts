import { expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import { devMockSeed } from "./seeds";
import { MACHINES } from "../graphql/queries";

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
