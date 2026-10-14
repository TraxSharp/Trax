// End-to-end smoke of the mock urql client: seeded query -> cancel mutation -> refetch shows
// read-after-write -> subscription receives a simulated event. Run: npx tsx scripts/mock-client-smoke.ts
import { pipe, take, toPromise } from "wonka";
import { createMockClient } from "../src/mock/client";
import { createMockStore } from "../src/mock/store/mock-store";
import { workQueueSeed } from "../src/mock/seeds";
import { startTrainEventSimulator } from "../src/mock/simulator";
import { WORK_QUEUE } from "../src/graphql/queries";
import { CANCEL_WORK_QUEUE_ENTRIES } from "../src/graphql/mutations";
import { ON_TRAIN_STATE_CHANGED } from "../src/graphql/subscriptions";

type Row = { id: number; status: string };
const rows = (r: unknown): Row[] =>
  (r as { data: { operations: { workQueue: { workQueues: { items: Row[] } } } } }).data
    .operations.workQueue.workQueues.items;

const store = createMockStore({ exposeOnWindow: false });
// fixtures off so this stays pinned to the seed's known ids (501/502), like the stories.
const client = createMockClient({ overrides: workQueueSeed, store, fixtures: false });

const netOnly = { requestPolicy: "network-only" as const };

// 1. initial query
const first = await client.query(WORK_QUEUE, { take: 25 }, netOnly).toPromise();
const before = rows(first);
console.log("initial:    ", before.map((r) => `${r.id}:${r.status}`).join("  "));

// 2. cancel two queued entries
const cancel = await client.mutation(CANCEL_WORK_QUEUE_ENTRIES, { ids: [501, 502] }).toPromise();
const ack = (cancel as { data: { operations: { workQueue: { cancelWorkQueueEntries: unknown } } } })
  .data.operations.workQueue.cancelWorkQueueEntries;
console.log("cancel ACK: ", JSON.stringify(ack));

// 3. refetch — 501/502 should now be CANCELLED, 503 still QUEUED (read-after-write)
const second = await client.query(WORK_QUEUE, { take: 25 }, netOnly).toPromise();
const after = rows(second);
console.log("after:      ", after.map((r) => `${r.id}:${r.status}`).join("  "));

// 4. subscription + simulator
const stop = startTrainEventSimulator(store, { intervalMs: 50 });
const event = await pipe(client.subscription(ON_TRAIN_STATE_CHANGED, {}), take(1), toPromise);
stop();
const ev = (event as { data: { onTrainStateChanged: { trainName: string; trainState: string } } })
  .data.onTrainStateChanged;
console.log("sub event:  ", `${ev.trainName} -> ${ev.trainState}`);

// assertions
const byId = (id: number) => after.find((r) => r.id === id)?.status;
const pass =
  byId(501) === "CANCELLED" &&
  byId(502) === "CANCELLED" &&
  byId(503) === "QUEUED" &&
  byId(504) === "DISPATCHED" &&
  Boolean(ev.trainName) &&
  (store.getState().workQueueCancelledIds as number[]).length === 2;

console.log(pass ? "\nPASS — read-after-write + subscription work" : "\nFAIL");
process.exit(pass ? 0 : 1);
