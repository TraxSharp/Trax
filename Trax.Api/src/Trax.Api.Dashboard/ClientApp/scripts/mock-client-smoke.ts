// End-to-end smoke of the mock console client: seeded query -> cancel mutation -> refetch shows
// read-after-write -> subscription receives a simulated event. Run: npx tsx scripts/mock-client-smoke.ts
import { createMockClient } from "../src/mock/client";
import { createMockStore } from "../src/mock/store/mock-store";
import { workQueueSeed } from "../src/mock/seeds";
import { startTrainEventSimulator } from "../src/mock/simulator";
import {
  cancelWorkQueueEntriesDocument,
  onTrainStateChangedDocument,
  workQueueDocument,
} from "../src/trax/index.g";

type Row = { id: number; status: string };
const rows = (r: unknown): Row[] =>
  (r as { data: { operations: { workQueue: { workQueues: { items: Row[] } } } } }).data
    .operations.workQueue.workQueues.items;

const store = createMockStore({ exposeOnWindow: false });
// fixtures off so this stays pinned to the seed's known ids (501/502), like the stories.
const client = createMockClient({ overrides: workQueueSeed, store, fixtures: false });


// 1. initial query
const first = await client.query(workQueueDocument, { take: 25 });
const before = rows(first);
console.log("initial:    ", before.map((r) => `${r.id}:${r.status}`).join("  "));

// 2. cancel two queued entries
const cancel = await client.mutation(cancelWorkQueueEntriesDocument, { ids: [501, 502] });
const ack = (cancel as { data: { operations: { workQueue: { cancelWorkQueueEntries: unknown } } } })
  .data.operations.workQueue.cancelWorkQueueEntries;
console.log("cancel ACK: ", JSON.stringify(ack));

// 3. refetch — 501/502 should now be CANCELLED, 503 still QUEUED (read-after-write)
const second = await client.query(workQueueDocument, { take: 25 });
const after = rows(second);
console.log("after:      ", after.map((r) => `${r.id}:${r.status}`).join("  "));

// 4. subscription + simulator
const stop = startTrainEventSimulator(store, { intervalMs: 50 });
const event = await new Promise<{ onTrainStateChanged: { trainName: string; trainState: string } }>((resolve, reject) => {
  const unsubscribe = client.subscribe(
    onTrainStateChangedDocument,
    {},
    (data) => {
      unsubscribe();
      resolve(data);
    },
    reject,
  );
});
stop();
const ev = event.onTrainStateChanged;
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
