import { expect, test } from "vitest";
import { RUN_LINKS, TRAIN_CHAINS } from "../graphql/queries";
import type { RunLinks, TrainChain } from "../types";
import { createMockClient, createMockStore } from "./index";
import { executionDetailScenario, MACHINE_IDS, trainsScenario } from "./scenarios";

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

test("RUN_LINKS: what a run is linked to, the invoking instance named without its owner", async () => {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: executionDetailScenario });

  const invoked = await client.query(RUN_LINKS, { id: 903 }).toPromise();
  expect(invoked.error).toBeUndefined();
  const links = invoked.data.operations.executionDetail as RunLinks;
  expect(links.invokedBy).toMatchObject({ machine: "source-partition", instanceId: MACHINE_IDS.dispatched, ownerKind: "SYSTEM", rowId: 11 });
  expect(links.workQueueEntryId).toBe(504);
  expect(links.deadLetterId).toBeNull();

  const retry = (await client.query(RUN_LINKS, { id: 951 }).toPromise()).data.operations.executionDetail as RunLinks;
  expect(retry.deadLetterId).toBe(31);
  expect(retry.invokedBy).toBeNull();
});
