import { expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import {
  acknowledgeDeadLetterDocument,
  bulkOperationDocument,
  bulkOperationsDocument,
  cancelExecutionDocument,
  deadLetterDetailDocument,
  deadLettersDocument,
  executionDetailDocument,
  manifestGroupsDocument,
  manifestsDocument,
  schedulerConfigDocument,
  startBulkOperationDocument,
  updateManifestDocument,
  updateManifestGroupDocument,
  updateSchedulerDocument,
} from "../trax/index.g";

// Client-level read-after-write for the Phase 2 overlays, over the captured fixtures.
function client() {
  return createMockClient({ store: createMockStore({ exposeOnWindow: false }) });
}
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);
type Row = Record<string, unknown> & { id: number };
const items = (r: unknown, path: string) => get(r, path) as Row[];

test("acknowledge dead letter -> list + detail show ACKNOWLEDGED with the note", async () => {
  const c = client();
  const list = await c.query(deadLettersDocument, { take: 25 });
  const id = items(list.data, "operations.deadLetters.deadLetters.items")[0].id;

  await c.mutation(acknowledgeDeadLetterDocument, { id, note: "handled" });

  const after = await c.query(deadLettersDocument, { take: 25 });
  const row = items(after.data, "operations.deadLetters.deadLetters.items").find((r) => r.id === id) as Row;
  expect(row.status).toBe("ACKNOWLEDGED");
  expect(row.resolutionNote).toBe("handled");

  const detail = await c.query(deadLetterDetailDocument, { id });
  expect(get(detail.data, "operations.deadLetters.deadLetter.status")).toBe("ACKNOWLEDGED");
});

test("update manifest -> list reflects the patched fields", async () => {
  const c = client();
  const list = await c.query(manifestsDocument, { take: 25 });
  const id = items(list.data, "operations.manifests.items")[0].id;

  await c.mutation(updateManifestDocument, { id, input: { priority: 99, isEnabled: false } });

  const after = await c.query(manifestsDocument, { take: 25 });
  const row = items(after.data, "operations.manifests.items").find((r) => r.id === id) as Row;
  expect(row.priority).toBe(99);
  expect(row.isEnabled).toBe(false);
});

test("update manifest group -> list reflects the patch", async () => {
  const c = client();
  const list = await c.query(manifestGroupsDocument, { take: 25 });
  const id = items(list.data, "operations.manifestGroups.groups.items")[0].id;

  await c.mutation(updateManifestGroupDocument, { id, input: { priority: 7 } });

  const after = await c.query(manifestGroupsDocument, { take: 25 });
  const row = items(after.data, "operations.manifestGroups.groups.items").find((r) => r.id === id) as Row;
  expect(row.priority).toBe(7);
});

test("update scheduler config -> config reflects the patch", async () => {
  const c = client();
  await c.mutation(updateSchedulerDocument, { input: { maxActiveJobs: 42 } });
  const after = await c.query(schedulerConfigDocument, {});
  expect(get(after.data, "operations.config.scheduler.maxActiveJobs")).toBe(42);
});

test("cancel execution -> detail shows cancellationRequested", async () => {
  const c = client();
  await c.mutation(cancelExecutionDocument, { id: 1 });
  const detail = await c.query(executionDetailDocument, { id: 1 });
  expect(get(detail.data, "operations.executionDetail.cancellationRequested")).toBe(true);
});

test("start bulk operation -> bulkOperations and bulkOperation read it back", async () => {
  const c = client();
  const started = await c
    .mutation(startBulkOperationDocument, {
      kind: "CANCEL",
      filter: { executions: { trainState: "IN_PROGRESS" } },
      expectedCount: 12,
    });
  expect(get(started.data, "operations.startBulkOperation.success")).toBe(true);
  const id = get(started.data, "operations.startBulkOperation.operation.id") as string;

  const list = await c.query(bulkOperationsDocument, {});
  const rows = get(list.data, "operations.bulkOperations") as Record<string, unknown>[];
  expect(rows.map((r) => r.id)).toEqual([id]);

  const one = await c.query(bulkOperationDocument, { id });
  expect(get(one.data, "operations.bulkOperation.done")).toBe(12);
  expect(get(one.data, "operations.bulkOperation.filter.executions.trainState")).toBe("IN_PROGRESS");
});
