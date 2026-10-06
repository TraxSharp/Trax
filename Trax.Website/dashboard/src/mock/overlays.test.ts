import { expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import {
  DEAD_LETTERS,
  DEAD_LETTER_DETAIL,
  EXECUTION_DETAIL,
  MANIFESTS,
  MANIFEST_GROUPS,
  SCHEDULER_CONFIG,
} from "../graphql/queries";
import {
  ACKNOWLEDGE_DEAD_LETTER,
  CANCEL_EXECUTION,
  UPDATE_MANIFEST,
  UPDATE_MANIFEST_GROUP,
  UPDATE_SCHEDULER,
} from "../graphql/mutations";

// Client-level read-after-write for the Phase 2 overlays, over the captured fixtures.
function client() {
  return createMockClient({ store: createMockStore({ exposeOnWindow: false }) });
}
const NET = { requestPolicy: "network-only" as const };
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);
type Row = Record<string, unknown> & { id: number };
const items = (r: unknown, path: string) => get(r, path) as Row[];

test("acknowledge dead letter -> list + detail show ACKNOWLEDGED with the note", async () => {
  const c = client();
  const list = await c.query(DEAD_LETTERS, { take: 25 }, NET).toPromise();
  const id = items(list.data, "operations.deadLetters.deadLetters.items")[0].id;

  await c.mutation(ACKNOWLEDGE_DEAD_LETTER, { id, note: "handled" }).toPromise();

  const after = await c.query(DEAD_LETTERS, { take: 25 }, NET).toPromise();
  const row = items(after.data, "operations.deadLetters.deadLetters.items").find((r) => r.id === id) as Row;
  expect(row.status).toBe("ACKNOWLEDGED");
  expect(row.resolutionNote).toBe("handled");

  const detail = await c.query(DEAD_LETTER_DETAIL, { id }, NET).toPromise();
  expect(get(detail.data, "operations.deadLetters.deadLetter.status")).toBe("ACKNOWLEDGED");
});

test("update manifest -> list reflects the patched fields", async () => {
  const c = client();
  const list = await c.query(MANIFESTS, { take: 25 }, NET).toPromise();
  const id = items(list.data, "operations.manifests.items")[0].id;

  await c.mutation(UPDATE_MANIFEST, { id, input: { priority: 99, isEnabled: false } }).toPromise();

  const after = await c.query(MANIFESTS, { take: 25 }, NET).toPromise();
  const row = items(after.data, "operations.manifests.items").find((r) => r.id === id) as Row;
  expect(row.priority).toBe(99);
  expect(row.isEnabled).toBe(false);
});

test("update manifest group -> list reflects the patch", async () => {
  const c = client();
  const list = await c.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise();
  const id = items(list.data, "operations.manifestGroups.groups.items")[0].id;

  await c.mutation(UPDATE_MANIFEST_GROUP, { id, input: { priority: 7 } }).toPromise();

  const after = await c.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise();
  const row = items(after.data, "operations.manifestGroups.groups.items").find((r) => r.id === id) as Row;
  expect(row.priority).toBe(7);
});

test("update scheduler config -> config reflects the patch", async () => {
  const c = client();
  await c.mutation(UPDATE_SCHEDULER, { input: { maxActiveJobs: 42 } }).toPromise();
  const after = await c.query(SCHEDULER_CONFIG, {}, NET).toPromise();
  expect(get(after.data, "operations.config.scheduler.maxActiveJobs")).toBe(42);
});

test("cancel execution -> detail shows cancellationRequested", async () => {
  const c = client();
  await c.mutation(CANCEL_EXECUTION, { id: 1 }).toPromise();
  const detail = await c.query(EXECUTION_DETAIL, { id: 1 }, NET).toPromise();
  expect(get(detail.data, "operations.executionDetail.cancellationRequested")).toBe(true);
});
