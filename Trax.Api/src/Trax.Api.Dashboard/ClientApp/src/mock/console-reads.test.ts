import { expect, test } from "vitest";
import { ATTENTION, LOOKUP, RUN_LINKS, TRAIN_CHAINS } from "../graphql/queries";
import type { Attention, LookupMatch, RunLinks, TrainChain } from "../types";
import { createMockClient, createMockStore } from "./index";
import { attentionScenario, executionDetailScenario, lookupScenario, MACHINE_IDS, trainsScenario } from "./scenarios";

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

test("LOOKUP: exact matches only, a user's draft by its rowId and never its owner", async () => {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: lookupScenario });
  const find = async (text: string) =>
    (await client.query(LOOKUP, { text }).toPromise()).data.operations.lookup as LookupMatch[];

  expect(await find(` ${MACHINE_IDS.draftBuilding} `)).toMatchObject([
    { kind: "MACHINE_INSTANCE", id: MACHINE_IDS.draftBuilding, ownerKind: "USER", rowId: 21, machine: "topic-map" },
  ]);
  expect(await find("902")).toMatchObject([{ kind: "RUN", id: "902", matchedOn: "id" }]);
  expect(await find("Trax.Trains.Zebra")).toEqual([]);
});

test("ATTENTION: a paused dispatcher and dead letters grouped by train and reason, each with the link to its list", async () => {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: attentionScenario });
  const result = await client.query(ATTENTION, {}).toPromise();
  expect(result.error).toBeUndefined();

  const attention = result.data.operations.attention as Attention;
  expect(attention.scheduler).toMatchObject({ jobDispatcherEnabled: false, paused: true, link: "/system/scheduler" });
  expect(attention.deadLetters[0]).toMatchObject({ count: 2 });
  expect(attention.deadLetters[0].link).toMatch(/^\/runs\/dead-letters\?status=AWAITING_INTERVENTION&train=.+&reason=.+/);
  expect(attention.stuckInstances[0].link).toBe("/machines/order?state=AwaitingPayment&stuck=true");
});
