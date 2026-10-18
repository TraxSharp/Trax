import { expect, test } from "vitest";
import { TRAIN_CHAINS } from "../graphql/queries";
import type { TrainChain } from "../types";
import { createMockClient, createMockStore } from "./index";
import { trainsScenario } from "./scenarios";

// The mock answers the console's reads that no page uses yet, in the shapes the API serves them.

test("TRAIN_CHAINS: a train's declared chain with no run, every node not reached", async () => {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: trainsScenario });
  const result = await client.query(TRAIN_CHAINS, { hideAdmin: true }).toPromise();
  expect(result.error).toBeUndefined();

  const trains = result.data.operations.trains as TrainChain[];
  const zebra = trains.find((t) => t.fullName === "Trax.Trains.ZebraTrain")!;
  expect(zebra.asksDecisions).toBe(true);
  expect(zebra.chain.hasGraph).toBe(true);
  expect(zebra.chain.train).toBe("Trax.Trains.ZebraTrain");
  expect(zebra.chain.allNodes.length).toBeGreaterThan(0);
  expect(zebra.chain.allNodes.every((n) => n.state === "NOT_REACHED")).toBe(true);

  const mango = trains.find((t) => t.fullName === "Trax.Trains.MangoTrain")!;
  expect(mango.chain.hasGraph).toBe(false);
  expect(mango.asksDecisions).toBe(false);
});
